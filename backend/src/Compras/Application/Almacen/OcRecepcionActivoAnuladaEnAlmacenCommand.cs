using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Idempotencia;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Almacen;

/// <summary>
/// Despachado por <c>AlmacenEventListenerWorker</c> al recibir <c>almacen.recepcion_activo.anulada.v1</c>:
/// fija <c>CantidadRecibida</c> de la línea de OC al acumulado ya calculado por Almacén (baja lo recibido
/// y reabre la OC si estaba cerrada). Idempotente: dedupe por <c>compras.eventos_procesados</c> y,
/// además, fijar el mismo acumulado dos veces no cambia nada.
/// </summary>
public sealed record OcRecepcionActivoAnuladaEnAlmacenCommand(
    Guid EventoId,
    RecepcionActivoAnuladaAlmacenPayload Payload) : IRequest;

public sealed class OcRecepcionActivoAnuladaEnAlmacenHandler(
    ComprasDbContext db, IPublisher publisher, ILogger<OcRecepcionActivoAnuladaEnAlmacenHandler> logger)
    : IRequestHandler<OcRecepcionActivoAnuladaEnAlmacenCommand>
{
    public const string EventType = "almacen.recepcion_activo.anulada.v1";

    public async Task Handle(OcRecepcionActivoAnuladaEnAlmacenCommand request, CancellationToken cancellationToken)
    {
        if (await db.EventosProcesados.AsNoTracking().AnyAsync(e => e.EventoId == request.EventoId && e.EventoTipo == EventType, cancellationToken))
        {
            logger.LogDebug("[OcRecepcionActivoAnuladaEnAlmacenHandler] Evento {EventId} ya procesado — skip.", request.EventoId);
            return;
        }

        var p = request.Payload;
        var oc = await db.OrdenesCompra
            .Include(o => o.Lineas)
            .Include(o => o.Incidencias.Where(i => i.CerradaEn == null))
            .FirstOrDefaultAsync(o => o.Id == p.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException("ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró OC '{p.OrdenCompraId}' al anular la recepción de activos {p.RecepcionId}.");
        var linea = oc.Lineas.FirstOrDefault(l => l.Id == p.LineaOcId)
            ?? throw new BusinessRuleException("RECEPCION_LINEA_OC_NO_RESOLUBLE",
                $"La línea de OC {p.LineaOcId} de la recepción {p.RecepcionId} no existe en la OC {oc.Id}.");

        if (p.CantidadRecibidaAcumulada < linea.CantidadFacturadaNeta)
        {
            // Mismo criterio que la devolución 8.B: recibido no puede quedar bajo lo facturado (check de BD); Almacén ya lo bloquea.
            logger.LogError(
                "[OcRecepcionActivoAnuladaEnAlmacenHandler] Anulación {RecepcionId} omitida en línea {LineaOcId}: dejaría recibido ({Acumulado}) bajo lo facturado ({Facturada}).",
                p.RecepcionId, p.LineaOcId, p.CantidadRecibidaAcumulada, linea.CantidadFacturadaNeta);
        }
        else
        {
            var resultado = oc.RegistrarRecepcionLinea(p.LineaOcId, p.CantidadRecibidaAcumulada, p.OcurridoEn);
            if (resultado.Reabrierta is { } reabierta)
                await publisher.Publish(reabierta, cancellationToken);
        }

        db.EventosProcesados.Add(new EventoProcesado(request.EventoId, EventType,
            $"RecepcionActivo={p.RecepcionActivoId} Recepcion={p.RecepcionId} OC={p.OrdenCompraId}"));
        await db.SaveChangesAsync(cancellationToken);
    }
}
