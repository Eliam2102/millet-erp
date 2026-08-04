using MediatR;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration.Mappers;

public sealed class FacturaProveedorRegistradaMapper
    : INotificationHandler<FacturaProveedorRegistradaDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FacturaProveedorRegistradaMapper(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task Handle(FacturaProveedorRegistradaDomainEvent notification, CancellationToken cancellationToken)
    {
        var integration = new FacturaProveedorRegistradaIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            FacturaProveedorId: notification.FacturaProveedorId,
            OrdenCompraId: notification.OrdenCompraId,
            TotalFactura: notification.TotalFactura,
            Lineas: notification.Lineas
                .Select(l => new LineaFacturadaPayload(l.LineaFacturaId, l.LineaOcId, l.Cantidad, l.Importe))
                .ToList(),
            LineasAcumuladasOc: notification.LineasAcumuladasOc
                .Select(l => new LineaOcAcumuladaPayload(l.LineaOcId, l.CantidadAcumulada))
                .ToList());
        return _publisher.PublishAsync(integration, cancellationToken);
    }
}
