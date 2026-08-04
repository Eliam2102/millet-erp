using MediatR;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Events;

namespace Millet.Compras.Application.Eventos;

/// <summary>
/// Handler in-proc de <see cref="RequisicionCerradaManualmenteEvent"/>
/// (ADR-0043 R3): loggea estructurado el cierre manual con estado final,
/// motivo y actor. Simetría con <see cref="RequisicionCanceladaLoggingHandler"/>.
/// </summary>
public sealed class RequisicionCerradaManualmenteLoggingHandler
    : INotificationHandler<RequisicionCerradaManualmenteEvent>
{
    private readonly ILogger<RequisicionCerradaManualmenteLoggingHandler> _logger;

    public RequisicionCerradaManualmenteLoggingHandler(
        ILogger<RequisicionCerradaManualmenteLoggingHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(RequisicionCerradaManualmenteEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Requisición cerrada manualmente. RequisicionId={RequisicionId} EmpresaId={EmpresaId} EstadoFinal={EstadoFinal} MotivoId={MotivoId} ActorId={ActorId} OcurridoEn={OcurridoEn}",
            notification.RequisicionId,
            notification.EmpresaId,
            notification.EstadoFinal,
            notification.MotivoId,
            notification.ActorId,
            notification.OcurridoEn);
        return Task.CompletedTask;
    }
}
