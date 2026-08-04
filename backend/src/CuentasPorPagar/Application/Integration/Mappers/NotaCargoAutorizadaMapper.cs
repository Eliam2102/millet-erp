using MediatR;
using Millet.CuentasPorPagar.Domain.NotaCargo.Events;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration.Mappers;

public sealed class NotaCargoAutorizadaMapper : INotificationHandler<NotaCargoAutorizadaDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    public NotaCargoAutorizadaMapper(IIntegrationEventPublisher publisher) { _publisher = publisher; }

    public Task Handle(NotaCargoAutorizadaDomainEvent notification, CancellationToken cancellationToken) =>
        _publisher.PublishAsync(new NotaCargoAutorizadaIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            NotaCargoId: notification.NotaCargoId,
            ProveedorId: notification.ProveedorId,
            Monto: notification.Monto,
            FacturaOrigenId: notification.FacturaOrigenId), cancellationToken);
}
