using MediatR;
using Millet.Compras.Domain.Oc.Events;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Integration.Oc.Mappers;

public sealed class OcRechazadaMapper : INotificationHandler<OrdenCompraRechazadaEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public OcRechazadaMapper(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task Handle(OrdenCompraRechazadaEvent notification, CancellationToken cancellationToken)
    {
        var integration = new OcRechazadaIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            OrdenCompraId: notification.OrdenCompraId,
            Folio: notification.Folio,
            CompradorTitularId: notification.CompradorTitularId,
            UsuarioRechazadorId: notification.UsuarioRechazadorId,
            MotivoRechazoId: notification.MotivoRechazoId,
            MotivoRechazoTexto: notification.MotivoRechazoTexto);
        return _publisher.PublishAsync(integration, cancellationToken);
    }
}
