using MediatR;
using Millet.Compras.Domain.Oc.Events;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Integration.Oc.Mappers;

public sealed class OcCanceladaMapper : INotificationHandler<OrdenCompraCanceladaEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public OcCanceladaMapper(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task Handle(OrdenCompraCanceladaEvent notification, CancellationToken cancellationToken)
    {
        var integration = new OcCanceladaIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            OrdenCompraId: notification.OrdenCompraId,
            Folio: notification.Folio,
            CompradorTitularId: notification.CompradorTitularId,
            UsuarioCanceladorId: notification.UsuarioCanceladorId,
            MotivoCancelacionId: notification.MotivoCancelacionId,
            MotivoCancelacionTexto: notification.MotivoCancelacionTexto);
        return _publisher.PublishAsync(integration, cancellationToken);
    }
}
