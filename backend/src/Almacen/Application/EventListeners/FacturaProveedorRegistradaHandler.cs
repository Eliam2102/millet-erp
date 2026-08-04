using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Almacen.Domain.Idempotencia;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.EventListeners;

/// <summary>
/// Handler MediatR del evento <c>cuentas_por_pagar.factura.registrada.v1</c>
/// (F3-PR1). Triggered desde el listener Service Bus.
///
/// <para>
/// <b>Lógica</b>: si la factura corresponde a una OC que tiene
/// recepción Variante B pendiente de factura
/// (<c>factura_pendiente=true</c>), vincula la factura al movimiento.
/// Si no hay recepción Variante B pendiente, ignora silenciosamente
/// (Variante A ya conoce su factura al momento de registrar).
/// </para>
/// <para>
/// <b>Idempotencia</b>: el listener envoltorio asegura que el evento
/// se procesa una sola vez vía <see cref="EventoProcesado"/>. Este
/// handler asume que ya pasó el dedup.
/// </para>
/// </summary>
public sealed record FacturaProveedorRegistradaCommand(
    Guid EventId,
    FacturaProveedorRegistradaPayload Payload) : IRequest;

public sealed class FacturaProveedorRegistradaHandler
    : IRequestHandler<FacturaProveedorRegistradaCommand>
{
    public const string EventType = "cuentas_por_pagar.factura.registrada.v1";

    private readonly AlmacenDbContext _db;
    private readonly ILogger<FacturaProveedorRegistradaHandler> _logger;

    public FacturaProveedorRegistradaHandler(
        AlmacenDbContext db,
        ILogger<FacturaProveedorRegistradaHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Handle(
        FacturaProveedorRegistradaCommand request, CancellationToken cancellationToken)
    {
        var payload = request.Payload;

        // Busca recepciones Variante B pendientes de factura para esta OC.
        // Convención: factura_pendiente NO se materializa como columna en
        // F2-PR1; se infiere por (oc_id presente, packing_list_blob_ref
        // presente, factura_id NULL). Cuando aparezca el flag explícito,
        // bastará con substituir el predicado.
        var recepcionesPendientes = await _db.Movimientos
            .Where(m => m.Tipo == TipoMovimiento.EntradaCompra
                && m.OcId == payload.OrdenCompraId
                && m.PackingListBlobRef != null
                && m.FacturaId == null
                && m.Estado == EstadoMovimiento.Registrado)
            .ToListAsync(cancellationToken);

        if (recepcionesPendientes.Count == 0)
        {
            _logger.LogDebug(
                "FacturaProveedorRegistrada: OC {OcId} no tiene recepciones Variante B pendientes. Ignorando.",
                payload.OrdenCompraId);
            return;
        }

        // Vincula la factura a TODAS las recepciones pendientes de la OC
        // (puede haber varias parciales).
        foreach (var recepcion in recepcionesPendientes)
        {
            // Reflexión interna: ConciliarConFacturaProveedor es internal.
            typeof(MovimientoInventario)
                .GetMethod("ConciliarConFacturaProveedor",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(recepcion, new object?[] { payload.FacturaProveedorId });
        }

        // Registra el evento procesado (idempotencia A12). Se inserta
        // en la misma TX que el UPDATE de los movimientos.
        _db.Set<EventoProcesado>().Add(new EventoProcesado(
            eventoId: request.EventId,
            eventoTipo: EventType,
            observaciones: $"OC={payload.OrdenCompraId}, factura={payload.FacturaProveedorId}, recepciones={recepcionesPendientes.Count}"));

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "FacturaProveedorRegistrada procesada. OC={OcId} Factura={FacturaId} RecepcionesVinculadas={N}",
            payload.OrdenCompraId, payload.FacturaProveedorId, recepcionesPendientes.Count);
    }
}
