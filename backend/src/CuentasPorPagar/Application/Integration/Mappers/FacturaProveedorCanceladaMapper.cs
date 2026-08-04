using MediatR;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration.Mappers;

public sealed class FacturaProveedorCanceladaMapper
    : INotificationHandler<FacturaProveedorCanceladaDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FacturaProveedorCanceladaMapper(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task Handle(FacturaProveedorCanceladaDomainEvent notification, CancellationToken cancellationToken)
    {
        var integration = new FacturaProveedorCanceladaIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            FacturaProveedorId: notification.FacturaProveedorId,
            OrdenCompraId: notification.OrdenCompraId,
            Motivo: notification.Motivo.ToString(),
            MotivoTexto: notification.MotivoTexto);
        return _publisher.PublishAsync(integration, cancellationToken);
    }
}
