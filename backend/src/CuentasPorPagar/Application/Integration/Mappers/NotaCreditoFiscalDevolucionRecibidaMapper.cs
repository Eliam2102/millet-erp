using MediatR;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.Events;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration.Mappers;

public sealed class NotaCreditoFiscalDevolucionRecibidaMapper
    : INotificationHandler<NotaCreditoFiscalDevolucionRecibidaDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    public NotaCreditoFiscalDevolucionRecibidaMapper(IIntegrationEventPublisher publisher) { _publisher = publisher; }

    public Task Handle(NotaCreditoFiscalDevolucionRecibidaDomainEvent notification, CancellationToken cancellationToken) =>
        _publisher.PublishAsync(new NotaCreditoFiscalDevolucionRecibidaIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            NotaCreditoProveedorId: notification.NotaCreditoProveedorId,
            NotaCargoId: notification.NotaCargoId,
            DevolucionAProveedorId: notification.DevolucionAProveedorId,
            ProveedorId: notification.ProveedorId,
            Total: notification.Total,
            UuidCfdi: notification.UuidCfdi), cancellationToken);
}
