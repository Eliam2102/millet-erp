using MediatR;
using Millet.Compras.Domain.Events;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Integration.Mappers;

public sealed class RequisicionEliminadaMapper : INotificationHandler<RequisicionEliminadaEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public RequisicionEliminadaMapper(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task Handle(RequisicionEliminadaEvent notification, CancellationToken cancellationToken)
    {
        var integration = new RequisicionEliminadaIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            RequisicionId: notification.RequisicionId,
            MotivoId: notification.MotivoId,
            MotivoTexto: notification.MotivoTexto,
            ActorId: notification.ActorId);
        return _publisher.PublishAsync(integration, cancellationToken);
    }
}
