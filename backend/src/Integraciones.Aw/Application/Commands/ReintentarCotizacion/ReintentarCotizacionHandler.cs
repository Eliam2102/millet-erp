using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Integraciones.Aw.Application.IntegrationEvents;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Application.Commands.ReintentarCotizacion;

/// <summary>
/// Handler de <see cref="ReintentarCotizacionCommand"/>. Carga la
/// entidad, invoca el método de dominio <c>Reintentar()</c> (que valida
/// el estado), y emite <c>AwCotizacionRecibida</c> al Outbox para que
/// el <c>AwDropWorker</c> reactive el flujo.
/// </summary>
public sealed class ReintentarCotizacionHandler
    : IRequestHandler<ReintentarCotizacionCommand, ReintentarCotizacionResponse>
{
    private readonly IntegracionesAwDbContext _db;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IAgentRealtimePublisher _agentRealtime;
    private readonly IClock _clock;
    private readonly ILogger<ReintentarCotizacionHandler> _logger;

    public ReintentarCotizacionHandler(
        IntegracionesAwDbContext db,
        IIntegrationEventPublisher publisher,
        IAgentRealtimePublisher agentRealtime,
        IClock clock,
        ILogger<ReintentarCotizacionHandler> logger)
    {
        _db = db;
        _publisher = publisher;
        _agentRealtime = agentRealtime;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ReintentarCotizacionResponse> Handle(
        ReintentarCotizacionCommand request,
        CancellationToken cancellationToken)
    {
        var entidad = await _db.EntidadesExternas
            .FirstOrDefaultAsync(e => e.Id == request.CotizacionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "AW_COTIZACION_NO_ENCONTRADA",
                $"Cotización {request.CotizacionId} no encontrada.");

        // Valida estado y resetea contadores. Throws InvalidStateTransitionException
        // si Estado no es FailedDrop ni ManuallyResolved (mapea a 409).
        entidad.Reintentar();

        var now = _clock.UtcNow;

        // Re-emitir AwCotizacionRecibida para que AwDropWorker recoja la
        // entidad reactivada. El filename suggestion se reconstruye con la
        // misma convención del registro original (timestamp = ahora).
        await _publisher.PublishAsync(
            new AwCotizacionRecibida(
                EmpresaId: entidad.EmpresaId,
                OcurridoEn: now,
                AggregateId: entidad.Id,
                QuoteReference: entidad.ReferenciaExterna,
                Sucursal: entidad.Sucursal,
                FilenameSuggestion: BuildFilename(entidad.ReferenciaExterna, now)),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        // Agent realtime push (best-effort) — la UI del vendedor ve la
        // cotización moverse a Submitted cuando el operador reintenta.
        await _agentRealtime.PublishCotizacionActualizadaAsync(entidad, cancellationToken);

        _logger.LogInformation(
            "Cotización {CotizacionId} ({QuoteReference}) reintentada. Razón: {Razon}",
            entidad.Id, entidad.ReferenciaExterna, request.Razon ?? "(no especificada)");

        return new ReintentarCotizacionResponse(
            Id: entidad.Id,
            QuoteReference: entidad.ReferenciaExterna,
            Estado: entidad.Estado,
            ReintentadoEn: now);
    }

    /// <summary>
    /// Filename para el drop EDI. Mismo patrón que <c>RegistrarCotizacionEdi</c>
    /// pero con timestamp: <c>cot_{quoteRef}-{utcTimestamp}.edi</c>. El worker
    /// lo pasa al adapter HTTP via header <c>X-Filename</c>.
    /// </summary>
    // Filename del reintento: cot_<REF>-<ts>.edi. El depto viaja dentro del
    // EDI (ya no se embebe la sucursal en el nombre). El timestamp evita
    // colisión con el archivo del registro original que pudo haber quedado
    // huérfano en el drop service.
    private static string BuildFilename(string quoteReference, DateTimeOffset whenUtc) =>
        $"cot_{quoteReference}-{whenUtc:yyyyMMddHHmmss}.edi";
}
