using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Idempotencia;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.EventListeners;

/// <summary>
/// Espejo del payload de CxP <c>cuentas_por_pagar.nota-credito.registrada.v1</c>.
/// Almacén suscribe pero solo procesa los eventos con
/// <c>TipoRelacionCfdi=3</c> (devolución, según c_TipoRelacion del SAT).
/// </summary>
public sealed record NotaCreditoProveedorRegistradaPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid NotaCreditoId,
    Guid ProveedorId,
    Guid? FacturaOrigenId,
    int TipoRelacionCfdi,
    decimal Total);

/// <summary>
/// Handler MediatR del evento de CxP. Filtra por
/// <c>TipoRelacionCfdi=3</c> (NC fiscal por devolución) y vincula la
/// NC a la <see cref="DevolucionAProveedor"/> pendiente matching por
/// proveedor + factura origen (F6-PR1).
///
/// <para>
/// <b>Cierre del ciclo bidireccional</b>: la devolución pasa a
/// <see cref="EstadoDevolucionProveedor.ConciliadaConNcFiscal"/>. Si
/// hay más de una devolución pendiente del mismo proveedor, se
/// concilia la más antigua que matchee la factura origen (heurística
/// simple — el caso "varias devoluciones al mismo proveedor por la
/// misma factura" no es típico; si pasa, se documenta y resuelve
/// manualmente).
/// </para>
/// </summary>
public sealed record NotaCreditoProveedorRegistradaCommand(
    Guid EventId,
    NotaCreditoProveedorRegistradaPayload Payload) : IRequest;

public sealed class NotaCreditoProveedorRegistradaHandler
    : IRequestHandler<NotaCreditoProveedorRegistradaCommand>
{
    public const string EventType = "cuentas_por_pagar.nota-credito.registrada.v1";

    /// <summary>
    /// Tipo de relación CFDI 03 = "Devolución de la mercancía o nota
    /// de crédito de los documentos relacionados" (catálogo SAT).
    /// </summary>
    public const int TipoCfdiDevolucion = 3;

    private readonly AlmacenDbContext _db;
    private readonly ILogger<NotaCreditoProveedorRegistradaHandler> _logger;

    public NotaCreditoProveedorRegistradaHandler(
        AlmacenDbContext db,
        ILogger<NotaCreditoProveedorRegistradaHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Handle(
        NotaCreditoProveedorRegistradaCommand request, CancellationToken cancellationToken)
    {
        var payload = request.Payload;

        if (payload.TipoRelacionCfdi != TipoCfdiDevolucion)
        {
            _logger.LogDebug(
                "NotaCreditoProveedorRegistrada con TipoRelacionCfdi={Tipo}; Almacén solo procesa tipo {Devolucion}. Ignorando.",
                payload.TipoRelacionCfdi, TipoCfdiDevolucion);
            // Aún registramos el evento como procesado para que
            // el listener no lo re-ofrezca al worker.
            _db.Set<EventoProcesado>().Add(new EventoProcesado(
                request.EventId, EventType,
                $"TipoRelacionCfdi={payload.TipoRelacionCfdi} (no devolución, ignorado)"));
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        // Busca devoluciones Registrada del proveedor (más reciente primero).
        // Si tenemos factura origen del payload, filtramos por esa también
        // — match más preciso.
        var query = _db.Set<DevolucionAProveedor>()
            .Where(d => d.ProveedorId == payload.ProveedorId
                && d.Estado == EstadoDevolucionProveedor.Registrada);
        if (payload.FacturaOrigenId is Guid facturaId)
        {
            query = query.Where(d => d.FacturaProveedorOrigenId == facturaId);
        }

        var devolucion = await query
            .OrderBy(d => d.RegistradaAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (devolucion is null)
        {
            _logger.LogInformation(
                "NotaCreditoProveedorRegistrada (devolución): no hay devolución pendiente para proveedor={Proveedor} factura={Factura}. Marcando evento procesado y dejando pendiente conciliación manual.",
                payload.ProveedorId, payload.FacturaOrigenId);
            _db.Set<EventoProcesado>().Add(new EventoProcesado(
                request.EventId, EventType,
                $"Sin devolución matching para proveedor={payload.ProveedorId}"));
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        devolucion.ConciliarConNcFiscal(payload.NotaCreditoId);

        _db.Set<EventoProcesado>().Add(new EventoProcesado(
            eventoId: request.EventId,
            eventoTipo: EventType,
            observaciones: $"DevolucionId={devolucion.Id} NcFiscalId={payload.NotaCreditoId}"));

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "DevolucionAProveedor {DevId} conciliada con NC fiscal {NcId} (proveedor={Proveedor}).",
            devolucion.Id, payload.NotaCreditoId, payload.ProveedorId);
    }
}
