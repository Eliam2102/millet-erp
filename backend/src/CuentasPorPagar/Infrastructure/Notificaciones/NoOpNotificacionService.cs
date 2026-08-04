using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Notificaciones;

namespace Millet.CuentasPorPagar.Infrastructure.Notificaciones;

/// <summary>
/// Stub que loggea la notificación sin emitirla (F4-PR2). PLATFORM-TODO
/// (&lt;NotificacionesGlobal&gt;): cuando el módulo Notificaciones
/// cross-ERP esté disponible, reemplazar por el adapter real.
/// </summary>
public sealed class NoOpNotificacionService : INotificacionService
{
    private readonly ILogger<NoOpNotificacionService> _logger;

    public NoOpNotificacionService(ILogger<NoOpNotificacionService> logger) { _logger = logger; }

    public Task NotificarSlaRevisionAsync(SlaRevisionNotificacion notificacion, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[NoOpNotificacionService] SLA {Nivel} factura={Factura} dep={Dep} motivo={Motivo} dias={Dias}/{SlaDias}",
            notificacion.Nivel, notificacion.FacturaProveedorId, notificacion.DependenciaRevisoraId,
            notificacion.MotivoRevisionId, notificacion.DiasEnRevision, notificacion.SlaDias);
        return Task.CompletedTask;
    }
}
