using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Idempotencia;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Almacen;

/// <summary>
/// Despachado por <c>AlmacenEventListenerWorker</c> al recibir <c>almacen.oc_devolucion.conciliada.v1</c>
/// (la NC fiscal 03 formalizó la nota de cargo de la devolución): fija <c>CantidadAcreditada</c> por línea.
/// Lo acreditado deja de esperarse, así la OC puede cerrarse sin reposición (decisión del owner, 30-oct).
/// Idempotente: dedupe por <c>compras.eventos_procesados</c> y el payload trae acumulados.
/// </summary>
public sealed record OcDevolucionConciliadaEnAlmacenCommand(
    Guid EventoId,
    OcDevolucionConciliadaAlmacenPayload Payload) : IRequest;

public sealed class OcDevolucionConciliadaEnAlmacenHandler(
    ComprasDbContext db, IPublisher publisher, ILogger<OcDevolucionConciliadaEnAlmacenHandler> logger)
    : IRequestHandler<OcDevolucionConciliadaEnAlmacenCommand>
{
    public const string EventType = "almacen.oc_devolucion.conciliada.v1";

    public async Task Handle(OcDevolucionConciliadaEnAlmacenCommand request, CancellationToken cancellationToken)
    {
        if (await db.EventosProcesados.AsNoTracking().AnyAsync(e => e.EventoId == request.EventoId && e.EventoTipo == EventType, cancellationToken))
        {
            logger.LogDebug("[OcDevolucionConciliadaEnAlmacenHandler] Evento {EventId} ya procesado — skip.", request.EventoId);
            return;
        }

        var p = request.Payload;
        var oc = await db.OrdenesCompra
            .Include(o => o.Lineas)
            .Include(o => o.Incidencias.Where(i => i.CerradaEn == null))
            .FirstOrDefaultAsync(o => o.Id == p.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException("ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró OC '{p.OrdenCompraId}' al conciliar la devolución {p.DevolucionId}.");

        // Aplica todas las líneas y publica solo el último cambio de estado (una OC, un SaveChanges).
        Domain.Oc.RecalcularSubEstadosResultado? ultimo = null;
        foreach (var linea in p.LineasAcreditadasOc)
            ultimo = oc.RegistrarAcreditacionLinea(linea.LineaOcId, linea.CantidadAcreditadaAcumulada, p.OcurridoEn);

        // Igual que FacturaProveedorRegistradaListener: se publica antes de guardar para que el outbox viaje en la misma transacción.
        if (ultimo?.Cerrada is { } cerrada) await publisher.Publish(cerrada, cancellationToken);
        if (ultimo?.Reabrierta is { } reabierta) await publisher.Publish(reabierta, cancellationToken);

        db.EventosProcesados.Add(new EventoProcesado(request.EventoId, EventType,
            $"Devolucion={p.DevolucionId} OC={p.OrdenCompraId}"));
        await db.SaveChangesAsync(cancellationToken);
    }
}
