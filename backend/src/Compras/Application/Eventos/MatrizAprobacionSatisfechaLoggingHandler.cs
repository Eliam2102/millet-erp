using MediatR;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Events;

namespace Millet.Compras.Application.Eventos;

/// <summary>
/// Handler in-proc de <see cref="MatrizAprobacionSatisfechaEvent"/>
/// (F4-PR4): loggea estructurado para auditoría / telemetría. Los logs
/// estructurados van a Application Insights cuando
/// <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> está configurada
/// (Program.cs wirea OpenTelemetry Distro).
///
/// <para>
/// Sin side-effects de negocio. Si se requiere persistir en
/// <c>core.audit_log</c> u otra tabla, ese trabajo viene en una fase
/// posterior con su propio handler dedicado.
/// </para>
/// </summary>
public sealed class MatrizAprobacionSatisfechaLoggingHandler
    : INotificationHandler<MatrizAprobacionSatisfechaEvent>
{
    private readonly ILogger<MatrizAprobacionSatisfechaLoggingHandler> _logger;

    public MatrizAprobacionSatisfechaLoggingHandler(ILogger<MatrizAprobacionSatisfechaLoggingHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(MatrizAprobacionSatisfechaEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Matriz aprobación satisfecha. RequisicionId={RequisicionId} EmpresaId={EmpresaId} OcurridoEn={OcurridoEn}",
            notification.RequisicionId,
            notification.EmpresaId,
            notification.OcurridoEn);
        return Task.CompletedTask;
    }
}
