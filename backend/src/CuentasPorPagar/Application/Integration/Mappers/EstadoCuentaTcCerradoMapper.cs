using MediatR;
using Millet.CuentasPorPagar.Domain.TarjetaCredito.Events;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration.Mappers;

public sealed class EstadoCuentaTcCerradoMapper : INotificationHandler<EstadoCuentaTcCerradoDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    public EstadoCuentaTcCerradoMapper(IIntegrationEventPublisher publisher) { _publisher = publisher; }

    public Task Handle(EstadoCuentaTcCerradoDomainEvent notification, CancellationToken cancellationToken) =>
        _publisher.PublishAsync(new EstadoCuentaTcCerradoIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            EstadoCuentaTcId: notification.EstadoCuentaTcId,
            TarjetaId: notification.TarjetaId,
            TotalBancoMxn: notification.TotalBancoMxn,
            FacturaProveedorId: notification.FacturaProveedorId,
            DiferenciaCambiariaMxn: notification.DiferenciaCambiariaMxn), cancellationToken);
}
