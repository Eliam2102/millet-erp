using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Domain.Exceptions;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Application.Commands.MarcarResueltoManual;

public sealed class MarcarResueltoManualHandler
    : IRequestHandler<MarcarResueltoManualCommand, MarcarResueltoManualResponse>
{
    private readonly IntegracionesAwDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAgentRealtimePublisher _agentRealtime;
    private readonly IClock _clock;
    private readonly ILogger<MarcarResueltoManualHandler> _logger;

    public MarcarResueltoManualHandler(
        IntegracionesAwDbContext db,
        ICurrentUserContext currentUser,
        IAgentRealtimePublisher agentRealtime,
        IClock clock,
        ILogger<MarcarResueltoManualHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _agentRealtime = agentRealtime;
        _clock = clock;
        _logger = logger;
    }

    public async Task<MarcarResueltoManualResponse> Handle(
        MarcarResueltoManualCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Nota))
        {
            throw new BusinessRuleException(
                "AW_NOTA_REQUERIDA",
                "Marcar como resuelto manualmente requiere una nota del operador.");
        }

        if (_currentUser.UserId is not Guid operadorId)
        {
            throw new ForbiddenException(
                "AW_OPERADOR_REQUERIDO",
                "MarcarResueltoManual requiere un usuario autenticado (no es operación SP).");
        }

        var entidad = await _db.EntidadesExternas
            .FirstOrDefaultAsync(e => e.Id == request.CotizacionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "AW_COTIZACION_NO_ENCONTRADA",
                $"Cotización {request.CotizacionId} no encontrada.");

        // El command rechaza marcar como resuelto algo en flight
        // (Submitted = pre-drop). El aggregate permite la transición
        // técnicamente, pero el caso de uso administrativo requiere esperar
        // al desenlace natural antes de intervenir manualmente. Si se necesita
        // forzar resolución en flight, abrir PLATFORM-TODO(<ForceResolveInFlight>).
        if (entidad.Estado != EstadoEntidad.FailedDrop
            && entidad.Estado != EstadoEntidad.FailedCorrelation)
        {
            throw new InvalidStateTransitionException(entidad.Estado, nameof(MarcarResueltoManual));
        }

        entidad.MarcarResueltoManual(request.Nota, operadorId);

        await _db.SaveChangesAsync(cancellationToken);

        // Agent realtime push (best-effort) — la UI del vendedor ve la
        // cotización en ManuallyResolved cuando el operador la cierra.
        await _agentRealtime.PublishCotizacionActualizadaAsync(entidad, cancellationToken);

        var now = _clock.UtcNow;
        _logger.LogInformation(
            "Cotización {CotizacionId} ({QuoteReference}) marcada como ManuallyResolved por operador {OperadorId}.",
            entidad.Id, entidad.ReferenciaExterna, operadorId);

        return new MarcarResueltoManualResponse(
            Id: entidad.Id,
            QuoteReference: entidad.ReferenciaExterna,
            Estado: entidad.Estado,
            ResolutionNote: entidad.ResolutionNote ?? string.Empty,
            ResueltoEn: now);
    }
}
