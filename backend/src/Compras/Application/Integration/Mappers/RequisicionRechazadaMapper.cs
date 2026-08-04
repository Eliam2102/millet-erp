using MediatR;
using Millet.Compras.Domain.Events;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Integration.Mappers;

public sealed class RequisicionRechazadaMapper : INotificationHandler<RequisicionRechazadaEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public RequisicionRechazadaMapper(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task Handle(RequisicionRechazadaEvent notification, CancellationToken cancellationToken)
    {
        var integration = new RequisicionRechazadaIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            RequisicionId: notification.RequisicionId,
            MotivoId: notification.MotivoId,
            MotivoTexto: notification.MotivoTexto,
            ActorId: notification.ActorId);
        return _publisher.PublishAsync(integration, cancellationToken);
    }
}
