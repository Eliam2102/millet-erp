using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Eventos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Application.EventListeners;

/// <summary>
/// Listener de <c>almacen.recepcion_activo.anulada.v1</c> (una por línea de OC). Marca la línea
/// como anulada en <c>recepciones_oc_local</c>: deja de contar para conciliar facturas y para
/// pendientes por facturar. Idempotente por <see cref="EventoProcesado"/> y por línea ya marcada.
/// Si la recepción aún no se proyectó lanza para que el Service Bus reintente (llegó antes que su alta).
/// </summary>
public sealed record RecepcionActivoAnuladaCommand(
    Guid EventId,
    RecepcionActivoAnuladaAlmacenPayload Payload) : IRequest;

public sealed class RecepcionActivoAnuladaHandler : IRequestHandler<RecepcionActivoAnuladaCommand>
{
    public const string EventType = "almacen.recepcion_activo.anulada.v1";

    private readonly CuentasPorPagarDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<RecepcionActivoAnuladaHandler> _logger;

    public RecepcionActivoAnuladaHandler(
        CuentasPorPagarDbContext db, IClock clock, ILogger<RecepcionActivoAnuladaHandler> logger)
    {
        _db = db; _clock = clock; _logger = logger;
    }

    public async Task Handle(RecepcionActivoAnuladaCommand request, CancellationToken cancellationToken)
    {
        if (await _db.EventosProcesados.AsNoTracking()
                .AnyAsync(e => e.EventoId == request.EventId && e.EventoTipo == EventType, cancellationToken))
        {
            _logger.LogDebug("[RecepcionActivoAnuladaHandler] Evento {EventId} ya procesado — skip.", request.EventId);
            return;
        }

        var p = request.Payload;
        var recepcion = await _db.RecepcionesOcLocal
            .FirstOrDefaultAsync(r => r.RecepcionId == p.RecepcionId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Recepción {p.RecepcionId} aún no proyectada en CxP; se reintenta la anulación.");

        recepcion.AnularLineaOc(p.LineaOcId);
        _db.EventosProcesados.Add(new EventoProcesado(
            eventoId: request.EventId,
            eventoTipo: EventType,
            procesadoEn: _clock.UtcNow,
            detalle: $"Recepcion={p.RecepcionId} LineaOc={p.LineaOcId} Cantidad={p.Cantidad}"));
        await _db.SaveChangesAsync(cancellationToken);
    }
}
