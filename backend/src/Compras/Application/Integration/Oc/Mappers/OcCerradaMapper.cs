using MediatR;
using Millet.Compras.Domain.Oc.Events;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Integration.Oc.Mappers;

public sealed class OcCerradaMapper : INotificationHandler<OrdenCompraCerradaEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public OcCerradaMapper(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task Handle(OrdenCompraCerradaEvent notification, CancellationToken cancellationToken)
    {
        var integration = new OcCerradaIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            OrdenCompraId: notification.OrdenCompraId,
            Folio: notification.Folio,
            CompradorTitularId: notification.CompradorTitularId);
        return _publisher.PublishAsync(integration, cancellationToken);
    }
}
