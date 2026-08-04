using MediatR;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Events;

namespace Millet.Compras.Application.Eventos;

/// <summary>
/// Handler in-proc de <see cref="RequisicionCerradaEvent"/> (F5-PR1):
/// loggea estructurado el cierre. Consistente con los demás handlers
/// de logging de Fase 4 / Fase 5.
/// </summary>
public sealed class RequisicionCerradaLoggingHandler : INotificationHandler<RequisicionCerradaEvent>
{
    private readonly ILogger<RequisicionCerradaLoggingHandler> _logger;

    public RequisicionCerradaLoggingHandler(ILogger<RequisicionCerradaLoggingHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(RequisicionCerradaEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Requisición cerrada. RequisicionId={RequisicionId} EmpresaId={EmpresaId} OcurridoEn={OcurridoEn}",
            notification.RequisicionId,
            notification.EmpresaId,
            notification.OcurridoEn);
        return Task.CompletedTask;
    }
}
