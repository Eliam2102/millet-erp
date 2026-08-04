using MediatR;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration.Mappers;

public sealed class FacturaProveedorRechazadaPorToleranciaMapper
    : INotificationHandler<FacturaProveedorRechazadaPorToleranciaDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FacturaProveedorRechazadaPorToleranciaMapper(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task Handle(FacturaProveedorRechazadaPorToleranciaDomainEvent notification, CancellationToken cancellationToken)
    {
        var integration = new FacturaProveedorRechazadaPorToleranciaIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            FacturaProveedorId: notification.FacturaProveedorId,
            OrdenCompraId: notification.OrdenCompraId,
            TotalFactura: notification.TotalFactura,
            TotalOc: notification.TotalOc,
            Diferencia: notification.Diferencia,
            ToleranciaAplicada: notification.ToleranciaAplicada);
        return _publisher.PublishAsync(integration, cancellationToken);
    }
}
