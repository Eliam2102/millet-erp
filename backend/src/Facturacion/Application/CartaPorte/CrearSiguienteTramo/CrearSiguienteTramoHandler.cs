using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Administracion.Domain;
using Millet.Facturacion.Application.CartaPorte.EmitirCartaPorte;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Dominio = Millet.Facturacion.Domain.CartaPorte;

namespace Millet.Facturacion.Application.CartaPorte.CrearSiguienteTramo;

/// <summary>
/// Crea y timbra la Carta Porte del siguiente tramo a partir de una previa:
/// hereda receptor/pedido/mercancías, fija <c>CartaPortePreviaId</c> y aplica los
/// datos del tramo nuevo. La previa no se toca (invariante 11).
/// </summary>
public sealed class CrearSiguienteTramoHandler : IRequestHandler<CrearSiguienteTramoCommand, EmitirCartaPorteResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ISender _sender;
    private readonly IPeriodoContablePort _periodo;
    private readonly ICfdiTimbradoPort _fiscal;
    private readonly ICfdiRepositorioPort _cfdiRepo;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly ICurrentUserContext _user;
    private readonly IClock _clock;

    public CrearSiguienteTramoHandler(
        FacturacionDbContext db, ISender sender, IPeriodoContablePort periodo, ICfdiTimbradoPort fiscal,
        ICfdiRepositorioPort cfdiRepo, ICurrentEmpresaContext empresa, ICurrentUserContext user, IClock clock)
    {
        _db = db;
        _sender = sender;
        _periodo = periodo;
        _fiscal = fiscal;
        _cfdiRepo = cfdiRepo;
        _empresa = empresa;
        _user = user;
        _clock = clock;
    }

    public async Task<EmitirCartaPorteResponse> Handle(CrearSiguienteTramoCommand command, CancellationToken cancellationToken)
    {
        if (_empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");

        var previa = await _db.CartasPorte
            .Include(c => c.Mercancias)
            .FirstOrDefaultAsync(c => c.Id == command.CartaPortePreviaId, cancellationToken)
            ?? throw new EntityNotFoundException("CARTA_PORTE_PREVIA_NO_ENCONTRADA", $"No existe la Carta Porte previa {command.CartaPortePreviaId}.");

        if (previa.Estado != EstadoTimbrado.Timbrado)
            throw new BusinessRuleException("CARTA_PORTE_PREVIA_NO_TIMBRADA", $"La Carta Porte previa debe estar timbrada (estado actual: {previa.Estado}).");

        var ahora = _clock.UtcNow;
        if (!await _periodo.EstaAbiertoAsync(ahora.Year, ahora.Month, cancellationToken))
            throw new BusinessRuleException("PERIODO_CERRADO", $"El período contable {ahora.Year}-{ahora.Month:D2} está cerrado; no se puede emitir.");

        var vehiculo = await _db.Vehiculos.FirstOrDefaultAsync(v => v.Id == command.VehiculoId, cancellationToken)
            ?? throw new EntityNotFoundException("VEHICULO_NO_ENCONTRADO", $"No existe el vehículo {command.VehiculoId}.");
        var operador = await _db.Operadores.FirstOrDefaultAsync(o => o.Id == command.OperadorId, cancellationToken)
            ?? throw new EntityNotFoundException("OPERADOR_NO_ENCONTRADO", $"No existe el operador {command.OperadorId}.");

        var tipoCfdi = command.TipoCfdi == "T" ? TipoComprobante.Traslado : TipoComprobante.Ingreso;

        // Receptor heredado de la previa (snapshot).
        var receptor = new DatosFiscalesReceptor(
            previa.ReceptorRfc, previa.ReceptorNombre, previa.ReceptorRegimenFiscal,
            previa.ReceptorCodigoPostal, previa.ReceptorUsoCfdi, previa.ReceptorPais, previa.ReceptorEsGenerico);

        var reserva = await _sender.Send(
            new ReservarFolioCommand(empresaId, command.SucursalId, TipoDocumentoSerie.Cfdi, DateOnly.FromDateTime(ahora.UtcDateTime)),
            cancellationToken);

        var cp = Dominio.CartaPorte.CrearBorrador(
            empresaId, tipoCfdi, reserva.Folio, reserva.Numero, command.SucursalId, previa.CajaId, _user.UserId,
            receptor, previa.SnapshotEmisor(), previa.Moneda, ahora.Year, ahora.Month,
            command.Origen, command.Destino, command.DistanciaKm, command.VehiculoId, command.OperadorId,
            cartaPortePreviaId: previa.Id, previa.PedidoFacturableId, command.FechaSalida, command.FechaLlegadaEstimada,
            command.MontoServicio, command.TasaIvaServicio,
            command.OrigenCodigoPostal, command.OrigenEstado, command.DestinoCodigoPostal, command.DestinoEstado);

        // Hereda las mercancías de la previa (misma carga, nuevo tramo).
        foreach (var m in previa.Mercancias)
            cp.AgregarMercancia(m.Descripcion, m.BienesTransp, m.ClaveUnidad, m.Cantidad, m.PesoEnKg, m.MaterialPeligroso);

        await TimbradoEjecutor.TimbrarYAplicarAsync(_db, 
            cp, CfdiEmisionBuilder.DesdeCartaPorte(cp, vehiculo, operador, ahora),
            _fiscal, _cfdiRepo, ahora, cancellationToken);

        _db.CartasPorte.Add(cp);
        await _db.SaveChangesAsync(cancellationToken);

        return new EmitirCartaPorteResponse(cp.Id, cp.Tipo.ToString(), cp.Estado.ToString(), cp.Uuid, cp.Folio, cp.Total, cp.CartaPortePreviaId);
    }
}
