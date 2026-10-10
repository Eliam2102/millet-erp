using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Almacen.Domain.Idempotencia;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.EventListeners;

public sealed record DiferenciaPrecioFacturaDetectadaCommand(
    Guid EventId, DiferenciaPrecioFacturaDetectadaPayload Payload) : IRequest;

/// <summary>D18 (8-oct): descarta también eventos antiguos pendientes; conserva el historial.</summary>
public sealed class DiferenciaPrecioFacturaDetectadaHandler(
    AlmacenDbContext db, ILogger<DiferenciaPrecioFacturaDetectadaHandler> logger)
    : IRequestHandler<DiferenciaPrecioFacturaDetectadaCommand>
{
    public const string EventType = "cuentas_por_pagar.factura.diferencia-precio-detectada.v1";

    public async Task Handle(DiferenciaPrecioFacturaDetectadaCommand request, CancellationToken cancellationToken)
    {
        if (await db.Set<EventoProcesado>().AnyAsync(e => e.EventoId == request.EventId && e.EventoTipo == EventType, cancellationToken)) return;
        db.Set<EventoProcesado>().Add(new EventoProcesado(request.EventId, EventType,
            $"D18: evento descartado sin revaluación. OC={request.Payload.OrdenCompraId}"));
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("D18: diferencia de precio {EventId} descartada sin movimientos ni valoración.", request.EventId);
    }
}
