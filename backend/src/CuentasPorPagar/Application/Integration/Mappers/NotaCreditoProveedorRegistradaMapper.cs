using MediatR;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.Events;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration.Mappers;

public sealed class NotaCreditoProveedorRegistradaMapper
    : INotificationHandler<NotaCreditoProveedorRegistradaDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public NotaCreditoProveedorRegistradaMapper(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task Handle(NotaCreditoProveedorRegistradaDomainEvent notification, CancellationToken cancellationToken)
    {
        var integration = new NotaCreditoProveedorRegistradaIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            NotaCreditoId: notification.NotaCreditoId,
            ProveedorId: notification.ProveedorId,
            FacturaOrigenId: notification.FacturaOrigenId,
            TipoRelacionCfdi: notification.TipoRelacionCfdi,
            Total: notification.Total,
            Uuid: notification.Uuid,
            Subtotal: notification.Subtotal,
            Iva: notification.Iva,
            RetencionesTotal: notification.RetencionesTotal,
            Retenciones: notification.Retenciones?.Select(r => new RetencionDetallePayload(r.Impuesto, r.Tasa, r.Importe)).ToList(),
            Moneda: notification.Moneda,
            TipoCambio: notification.TipoCambio,
            SucursalId: notification.SucursalId);
        return _publisher.PublishAsync(integration, cancellationToken);
    }
}
