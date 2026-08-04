using MediatR;
using Millet.Compras.Domain.Events;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Integration.Mappers;

public sealed class RequisicionCerradaMapper : INotificationHandler<RequisicionCerradaEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public RequisicionCerradaMapper(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task Handle(RequisicionCerradaEvent notification, CancellationToken cancellationToken)
    {
        var integration = new RequisicionCerradaIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            RequisicionId: notification.RequisicionId);
        return _publisher.PublishAsync(integration, cancellationToken);
    }
}
