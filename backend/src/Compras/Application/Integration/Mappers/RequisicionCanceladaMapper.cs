using MediatR;
using Millet.Compras.Domain.Events;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Integration.Mappers;

public sealed class RequisicionCanceladaMapper : INotificationHandler<RequisicionCanceladaEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public RequisicionCanceladaMapper(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task Handle(RequisicionCanceladaEvent notification, CancellationToken cancellationToken)
    {
        var integration = new RequisicionCanceladaIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            RequisicionId: notification.RequisicionId,
            MotivoId: notification.MotivoId,
            MotivoTexto: notification.MotivoTexto,
            ActorId: notification.ActorId);
        return _publisher.PublishAsync(integration, cancellationToken);
    }
}
