using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Administracion.Domain;
using Millet.Facturacion.Application.Facturas;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.NotasCredito.SolicitarReembolso;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.NotasCredito.EmitirNotaCreditoBonificacion;

/// <summary>
/// Orquesta la emisión de una NC por bonificación: carga la factura origen,
/// valida que esté timbrada y no cancelada (invariante 6), reserva folio de NC,
/// construye la NC con el receptor snapshot de la factura, la relaciona (01) y la
/// timbra (stub hasta F12). El receptor y el emisor se heredan de la factura
/// origen (ya validados al emitirla).
/// </summary>
public sealed class EmitirNotaCreditoBonificacionHandler
    : IRequestHandler<EmitirNotaCreditoBonificacionCommand, EmitirNotaCreditoBonificacionResponse>
{
    private readonly ValidadorReceptorFiscal _receptorFiscal;
    private readonly FacturacionDbContext _db;
    private readonly ISender _sender;
    private readonly IPeriodoContablePort _periodo;
    private readonly ICfdiTimbradoPort _fiscal;
    private readonly ICfdiRepositorioPort _cfdiRepo;
    private readonly IIntegrationEventPublisher _eventos;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly ICurrentUserContext _user;
    private readonly IClock _clock;
    private readonly ICurrentUserPermissions _permisos;
    private readonly ISaldoFacturaReadPort _saldos;

    public EmitirNotaCreditoBonificacionHandler(
        FacturacionDbContext db,
        ISender sender,
        IPeriodoContablePort periodo,
        ICfdiTimbradoPort fiscal,
        ICfdiRepositorioPort cfdiRepo,
        IIntegrationEventPublisher eventos,
        ICurrentEmpresaContext empresa,
        ICurrentUserContext user,
        IClock clock, ValidadorReceptorFiscal receptorFiscal, ICurrentUserPermissions permisos, ISaldoFacturaReadPort saldos)
    {
        _permisos = permisos;
        _saldos = saldos;
        _receptorFiscal = receptorFiscal;
        _db = db;
        _sender = sender;
        _periodo = periodo;
        _fiscal = fiscal;
        _cfdiRepo = cfdiRepo;
        _eventos = eventos;
        _empresa = empresa;
        _user = user;
        _clock = clock;
    }

    public async Task<EmitirNotaCreditoBonificacionResponse> Handle(
        EmitirNotaCreditoBonificacionCommand command,
        CancellationToken cancellationToken)
    {
        if (_empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");

        if (command.Reembolso is not null)
            await ReembolsoNotaCredito.ExigirPermisoAsync(_permisos, cancellationToken);

        var factura = await _db.FacturasVenta
            .FirstOrDefaultAsync(f => f.Id == command.FacturaVentaId, cancellationToken)
            ?? throw new EntityNotFoundException("FACTURA_NO_ENCONTRADA", $"No existe la factura {command.FacturaVentaId}.");

        // Invariante 6: NC sólo sobre factura no cancelada (y timbrada).
        if (factura.Estado == EstadoTimbrado.Cancelado)
            throw new BusinessRuleException(
                "FACTURA_CANCELADA",
                "No se puede emitir una nota de crédito sobre una factura cancelada.");
        if (factura.Estado != EstadoTimbrado.Timbrado)
            throw new BusinessRuleException(
                "FACTURA_NO_TIMBRADA",
                $"Solo se puede bonificar una factura timbrada (estado actual: {factura.Estado}).");

        // F1-CXC-05 Parte 2: sin saldos negativos. La NC no puede acreditar más de lo que la factura tiene pendiente
        // (antes CxC la rechazaba al proyectarla y quedaba en reintentos).
        var saldo = await SaldoPendienteFactura.CalcularAsync(_db, _saldos, factura.Id, factura.Total, cancellationToken);
        if (command.MontoTotal > saldo)
            throw new BusinessRuleException("NC_IMPORTE_MAYOR_SALDO",
                $"El importe de la nota ({command.MontoTotal:N2}) es mayor que el saldo pendiente de la factura ({saldo:N2}).");

        var ahora = _clock.UtcNow;
        var anio = ahora.Year;
        var mes = ahora.Month;

        if (!await _periodo.EstaAbiertoAsync(anio, mes, cancellationToken))
            throw new BusinessRuleException(
                "PERIODO_CERRADO",
                $"El período contable {anio}-{mes:D2} está cerrado; no se puede emitir.");

        await _receptorFiscal.ValidarComprobanteAsync(factura, cancellationToken);
        if (command.Reembolso is not null)
            await ReembolsoNotaCredito.ExigirClienteAsync(_db, factura, cancellationToken);

        var reserva = await _sender.Send(
            new ReservarFolioCommand(
                empresaId, factura.SucursalId, TipoDocumentoSerie.NotaCredito,
                DateOnly.FromDateTime(ahora.UtcDateTime)),
            cancellationToken);

        // Receptor snapshot heredado de la factura origen.
        var receptor = new DatosFiscalesReceptor(
            Rfc: factura.ReceptorRfc,
            Nombre: factura.ReceptorNombre,
            RegimenFiscal: factura.ReceptorRegimenFiscal,
            CodigoPostal: factura.ReceptorCodigoPostal,
            UsoCfdi: factura.ReceptorUsoCfdi,
            Pais: factura.ReceptorPais,
            EsGenerico: factura.ReceptorEsGenerico);

        var nc = NotaCredito.CrearBonificacion(
            empresaId: empresaId,
            folio: reserva.Folio,
            folioNumero: reserva.Numero,
            sucursalId: factura.SucursalId,
            cajaId: factura.CajaId,
            usuarioEmisorId: _user.UserId,
            receptor: receptor,
            emisor: factura.SnapshotEmisor(),
            formaPago: factura.FormaPago,
            moneda: factura.Moneda,
            tipoCambio: factura.TipoCambio,
            periodoAnio: anio,
            periodoMes: mes,
            canalVentaId: factura.CanalVentaId,
            facturaRelacionadaId: factura.Id,
            montoTotal: command.MontoTotal,
            tasaIva: command.TasaIva,
            descripcion: command.Descripcion);

        if (!string.IsNullOrWhiteSpace(factura.Uuid))
            nc.AgregarRelacion(factura.Uuid!, "01");
        if (command.Reembolso is { } reembolso)
            nc.ProgramarReembolso(reembolso.Banco, reembolso.Clabe, reembolso.Titular);

        await TimbradoEjecutor.TimbrarYAplicarAsync(_db, 
            nc, CfdiEmisionBuilder.DesdeNotaCredito(nc, ahora),
            _fiscal, _cfdiRepo, ahora, cancellationToken);

        // F10-PR1: evento de NC de bonificación timbrada.
        if (nc.Estado == EstadoTimbrado.Timbrado)
            await _eventos.PublishAsync(EventosContablesFacturacion.NotaCreditoTimbrada(
                nc, ahora, factura.Id, clienteId: await ClienteContableFacturacion.ResolverAsync(_db, factura, cancellationToken)),
                cancellationToken);

        // F6.3: con «Devolver dinero al cliente», la solicitud a Tesorería sale en la misma transacción que el timbre.
        await ReembolsoNotaCredito.SolicitarProgramadoSiAplicaAsync(_db, _eventos, nc, _user.UserId, ahora, cancellationToken);

        _db.NotasCredito.Add(nc);
        await _db.SaveChangesAsync(cancellationToken);

        return new EmitirNotaCreditoBonificacionResponse(
            Id: nc.Id,
            Estado: nc.Estado.ToString(),
            Uuid: nc.Uuid,
            Folio: nc.Folio,
            Total: nc.Total);
    }

}
