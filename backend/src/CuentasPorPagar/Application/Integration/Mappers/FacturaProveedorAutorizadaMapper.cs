using MediatR;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration.Mappers;

public sealed class FacturaProveedorAutorizadaMapper
    : INotificationHandler<FacturaProveedorAutorizadaDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FacturaProveedorAutorizadaMapper(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task Handle(FacturaProveedorAutorizadaDomainEvent notification, CancellationToken cancellationToken)
    {
        var integration = new FacturaProveedorAutorizadaIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.FechaAutorizacion,
            FacturaProveedorId: notification.FacturaProveedorId,
            OrdenCompraId: notification.OrdenCompraId);
        return _publisher.PublishAsync(integration, cancellationToken);
    }
}
