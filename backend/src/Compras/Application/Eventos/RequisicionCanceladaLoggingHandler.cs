using MediatR;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Events;

namespace Millet.Compras.Application.Eventos;

/// <summary>
/// Handler in-proc de <see cref="RequisicionCanceladaEvent"/> (F4-PR4):
/// loggea estructurado la cancelación con motivo + actor. Se agrega por
/// simetría con los otros handlers de la fase, aunque el breakdown
/// literal de F4-PR4 solo lista Matriz + Cubrimiento.
/// </summary>
public sealed class RequisicionCanceladaLoggingHandler
    : INotificationHandler<RequisicionCanceladaEvent>
{
    private readonly ILogger<RequisicionCanceladaLoggingHandler> _logger;

    public RequisicionCanceladaLoggingHandler(ILogger<RequisicionCanceladaLoggingHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(RequisicionCanceladaEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Requisición cancelada. RequisicionId={RequisicionId} EmpresaId={EmpresaId} MotivoId={MotivoId} ActorId={ActorId} OcurridoEn={OcurridoEn}",
            notification.RequisicionId,
            notification.EmpresaId,
            notification.MotivoId,
            notification.ActorId,
            notification.OcurridoEn);
        return Task.CompletedTask;
    }
}
