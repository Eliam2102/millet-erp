using MediatR;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Application.Integration.Mappers;

public sealed class DiferenciaPrecioFacturaDetectadaMapper
    : INotificationHandler<DiferenciaPrecioFacturaDetectadaDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DiferenciaPrecioFacturaDetectadaMapper(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task Handle(DiferenciaPrecioFacturaDetectadaDomainEvent notification, CancellationToken cancellationToken)
    {
        var integration = new DiferenciaPrecioFacturaDetectadaIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            FacturaProveedorId: notification.FacturaProveedorId,
            OrdenCompraId: notification.OrdenCompraId,
            ArticuloId: notification.ArticuloId,
            CantidadFacturada: notification.CantidadFacturada,
            PrecioFacturaUnitarioMxn: notification.PrecioFacturaUnitarioMxn,
            PrecioOcUnitarioMxn: notification.PrecioOcUnitarioMxn,
            DiferenciaUnitarioMxn: notification.DiferenciaUnitarioMxn,
            MontoDiferenciaTotalMxn: notification.MontoDiferenciaTotalMxn);
        return _publisher.PublishAsync(integration, cancellationToken);
    }
}
