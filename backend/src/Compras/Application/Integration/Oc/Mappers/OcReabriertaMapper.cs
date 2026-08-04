using MediatR;
using Millet.Compras.Domain.Oc.Events;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Integration.Oc.Mappers;

public sealed class OcReabriertaMapper : INotificationHandler<OrdenCompraReabriertaEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public OcReabriertaMapper(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task Handle(OrdenCompraReabriertaEvent notification, CancellationToken cancellationToken)
    {
        var integration = new OcReabriertaIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            OrdenCompraId: notification.OrdenCompraId,
            Folio: notification.Folio,
            CompradorTitularId: notification.CompradorTitularId);
        return _publisher.PublishAsync(integration, cancellationToken);
    }
}
