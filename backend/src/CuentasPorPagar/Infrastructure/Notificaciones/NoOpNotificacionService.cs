using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.CuentasPorPagar.Domain.Notificaciones;

namespace Millet.CuentasPorPagar.Infrastructure.Notificaciones;

/// <summary>
/// Loggea el aviso con los destinatarios configurados sin emitirlo (ambientes
/// sin Graph/Mail.Send). PLATFORM-TODO(&lt;NotificacionesGlobal&gt;): destinatarios
/// por nivel (área día 3, jefe + CxP día 5, Director de Finanzas día 10) cuando
/// exista el módulo Notificaciones cross-ERP.
/// La deduplicación ya la hace el worker con <c>avisos_sla_revision</c> y el
/// aviso visible para el área vive en la bandeja «Revisión por área».
/// </summary>
public sealed class NoOpNotificacionService : INotificacionService
{
    private readonly ILogger<NoOpNotificacionService> _logger;
    private readonly AvisosRevisionOptions _options;

    public NoOpNotificacionService(ILogger<NoOpNotificacionService> logger, IOptions<AvisosRevisionOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public Task NotificarSlaRevisionAsync(SlaRevisionNotificacion notificacion, CancellationToken cancellationToken)
    {
        var para = _options.Destinatarios(notificacion);
        if (para.Count == 0)
            _logger.LogWarning("[NoOpNotificacionService] Sin destinatarios en {Seccion}; el aviso no llegará a nadie.", AvisosRevisionOptions.SectionName);
        _logger.LogInformation(
            "[NoOpNotificacionService] SLA {Nivel} factura={Factura} folio={Folio} dep={Dep} motivo={Motivo} dias={Dias}/{SlaDias} Para={Destinatarios}",
            notificacion.Nivel, notificacion.FacturaProveedorId, notificacion.Folio, notificacion.DependenciaRevisoraId,
            notificacion.MotivoRevisionId, notificacion.DiasEnRevision, notificacion.SlaDias, string.Join(", ", para));
        return Task.CompletedTask;
    }
}
