using MediatR;
using Millet.Compras.Domain.Oc.Events;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Integration.Oc.Mappers;

public sealed class OcEnviadaAAutorizacionMapper
    : INotificationHandler<OrdenCompraEnviadaAAutorizacionEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public OcEnviadaAAutorizacionMapper(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task Handle(OrdenCompraEnviadaAAutorizacionEvent notification, CancellationToken cancellationToken)
    {
        var integration = new OcEnviadaAAutorizacionIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            OrdenCompraId: notification.OrdenCompraId,
            Folio: notification.Folio,
            CompradorTitularId: notification.CompradorTitularId);
        return _publisher.PublishAsync(integration, cancellationToken);
    }
}
