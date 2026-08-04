using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Administracion.Domain;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Dominio = Millet.Facturacion.Domain.CartaPorte;

namespace Millet.Facturacion.Application.CartaPorte.EmitirCartaPorte;

/// <summary>
/// Orquesta la emisión de una Carta Porte 3.1 (§4.6): período → valida
/// vehículo/operador → reserva folio → construye el CFDI (T o I) + mercancías →
/// timbra (stub hasta F12).
/// </summary>
public sealed class EmitirCartaPorteHandler : IRequestHandler<EmitirCartaPorteCommand, EmitirCartaPorteResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ISender _sender;
    private readonly IPeriodoContablePort _periodo;
    private readonly ICfdiTimbradoPort _fiscal;
    private readonly ICfdiRepositorioPort _cfdiRepo;
    private readonly IEmpresaFiscalReadPort _empresasFiscal;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly ICurrentUserContext _user;
    private readonly IClock _clock;

    public EmitirCartaPorteHandler(
        FacturacionDbContext db, ISender sender, IPeriodoContablePort periodo, ICfdiTimbradoPort fiscal,
        ICfdiRepositorioPort cfdiRepo, IEmpresaFiscalReadPort empresasFiscal,
        ICurrentEmpresaContext empresa, ICurrentUserContext user, IClock clock)
    {
        _db = db;
        _sender = sender;
        _periodo = periodo;
        _fiscal = fiscal;
        _cfdiRepo = cfdiRepo;
        _empresasFiscal = empresasFiscal;
        _empresa = empresa;
        _user = user;
        _clock = clock;
    }

    public async Task<EmitirCartaPorteResponse> Handle(EmitirCartaPorteCommand command, CancellationToken cancellationToken)
    {
        if (_empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");

        var ahora = _clock.UtcNow;
        if (!await _periodo.EstaAbiertoAsync(ahora.Year, ahora.Month, cancellationToken))
            throw new BusinessRuleException("PERIODO_CERRADO", $"El período contable {ahora.Year}-{ahora.Month:D2} está cerrado; no se puede emitir.");

        var (vehiculo, operador) = await CargarTransporteAsync(command.VehiculoId, command.OperadorId, cancellationToken);

        // F12-PR1: snapshot del emisor (falla sin CP fiscal, sin quemar folio).
        var emisor = await EmisorSnapshot.ResolverAsync(
            _empresasFiscal, empresaId, command.RfcEmisor, command.RegimenFiscalEmisor, cancellationToken);

        var tipoCfdi = command.TipoCfdi == "T" ? TipoComprobante.Traslado : TipoComprobante.Ingreso;

        var receptor = new DatosFiscalesReceptor(
            command.ReceptorRfc, command.ReceptorNombre, command.ReceptorRegimenFiscal,
            command.ReceptorCodigoPostal, command.ReceptorUsoCfdi, command.ReceptorPais,
            DatosFiscalesReceptor.EsRfcGenerico(command.ReceptorRfc));

        var reserva = await _sender.Send(
            new ReservarFolioCommand(empresaId, command.SucursalId, TipoDocumentoSerie.Cfdi, DateOnly.FromDateTime(ahora.UtcDateTime)),
            cancellationToken);

        var cp = Dominio.CartaPorte.CrearBorrador(
            empresaId, tipoCfdi, reserva.Folio, reserva.Numero, command.SucursalId, command.CajaId, _user.UserId,
            receptor, emisor, command.Moneda, ahora.Year, ahora.Month,
            command.Origen, command.Destino, command.DistanciaKm, command.VehiculoId, command.OperadorId,
            cartaPortePreviaId: null, command.PedidoFacturableId, command.FechaSalida, command.FechaLlegadaEstimada,
            command.MontoServicio, command.TasaIvaServicio,
            command.OrigenCodigoPostal, command.OrigenEstado, command.DestinoCodigoPostal, command.DestinoEstado);

        foreach (var m in command.Mercancias)
            cp.AgregarMercancia(m.Descripcion, m.BienesTransp, m.ClaveUnidad, m.Cantidad, m.PesoEnKg, m.MaterialPeligroso);

        await TimbradoEjecutor.TimbrarYAplicarAsync(_db, 
            cp, CfdiEmisionBuilder.DesdeCartaPorte(cp, vehiculo, operador, ahora),
            _fiscal, _cfdiRepo, ahora, cancellationToken);

        _db.CartasPorte.Add(cp);
        await _db.SaveChangesAsync(cancellationToken);

        return new EmitirCartaPorteResponse(cp.Id, cp.Tipo.ToString(), cp.Estado.ToString(), cp.Uuid, cp.Folio, cp.Total, cp.CartaPortePreviaId);
    }

    private async Task<(Dominio.Vehiculo Vehiculo, Dominio.Operador Operador)> CargarTransporteAsync(
        Guid vehiculoId, Guid operadorId, CancellationToken cancellationToken)
    {
        var vehiculo = await _db.Vehiculos.FirstOrDefaultAsync(v => v.Id == vehiculoId, cancellationToken)
            ?? throw new EntityNotFoundException("VEHICULO_NO_ENCONTRADO", $"No existe el vehículo {vehiculoId}.");
        var operador = await _db.Operadores.FirstOrDefaultAsync(o => o.Id == operadorId, cancellationToken)
            ?? throw new EntityNotFoundException("OPERADOR_NO_ENCONTRADO", $"No existe el operador {operadorId}.");
        return (vehiculo, operador);
    }
}
