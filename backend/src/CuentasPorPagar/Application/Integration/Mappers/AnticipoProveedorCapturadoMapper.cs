using MediatR;
using Millet.CuentasPorPagar.Domain.AnticipoProveedor.Events;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration.Mappers;

public sealed class AnticipoProveedorCapturadoMapper : INotificationHandler<AnticipoProveedorCapturadoDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    public AnticipoProveedorCapturadoMapper(IIntegrationEventPublisher publisher) { _publisher = publisher; }

    public Task Handle(AnticipoProveedorCapturadoDomainEvent notification, CancellationToken cancellationToken) =>
        _publisher.PublishAsync(new AnticipoProveedorCapturadoIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            AnticipoId: notification.AnticipoId,
            ProveedorId: notification.ProveedorId,
            MontoEntregado: notification.MontoEntregado,
            OrdenCompraId: notification.OrdenCompraId), cancellationToken);
}
