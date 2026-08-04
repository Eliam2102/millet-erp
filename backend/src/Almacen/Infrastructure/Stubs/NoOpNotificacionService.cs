using Microsoft.Extensions.Logging;
using Millet.Almacen.Domain.Ports.Notificaciones;

namespace Millet.Almacen.Infrastructure.Stubs;

/// <summary>
/// PLATFORM-TODO(&lt;NotificacionService&gt;): adapter real cuando exista
/// el módulo Notificaciones (ADR-0026). Hasta entonces el stub solo
/// logea — el worker `RegularizacionValeSlaWorker` registra el aviso
/// y los integradores con email/SignalR consumen el log cuando wireen.
/// </summary>
public sealed class NoOpNotificacionService : INotificacionService
{
    private readonly ILogger<NoOpNotificacionService> _logger;

    public NoOpNotificacionService(ILogger<NoOpNotificacionService> logger)
    {
        _logger = logger;
    }

    public Task NotificarValeSinRegularizarAsync(
        Guid movimientoValeId,
        string folioVale,
        Guid? personaDestinatariaId,
        DateTimeOffset fechaLimite,
        int diaDelSla,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "[NoOpNotificacionService] Vale sin regularizar — día {Dia} del SLA. " +
            "MovId={MovId} Folio={Folio} Destinatario={Destinatario} Limite={Limite}",
            diaDelSla, movimientoValeId, folioVale, personaDestinatariaId, fechaLimite);
        return Task.CompletedTask;
    }
}
