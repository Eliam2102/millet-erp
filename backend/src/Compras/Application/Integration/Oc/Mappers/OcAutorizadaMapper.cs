using MediatR;
using Millet.Compras.Domain.Oc.Events;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Integration.Oc.Mappers;

public sealed class OcAutorizadaMapper : INotificationHandler<OrdenCompraAutorizadaEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public OcAutorizadaMapper(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task Handle(OrdenCompraAutorizadaEvent notification, CancellationToken cancellationToken)
    {
        var integration = new OcAutorizadaIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            OrdenCompraId: notification.OrdenCompraId,
            Folio: notification.Folio,
            CompradorTitularId: notification.CompradorTitularId,
            FechaContabilizacion: notification.FechaContabilizacion);
        return _publisher.PublishAsync(integration, cancellationToken);
    }
}
