using MediatR;
using Millet.Compras.Domain.Events;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Compras.Application.Integration.Mappers;

/// <summary>
/// Mapper de <see cref="MatrizAprobacionSatisfechaEvent"/> →
/// <see cref="RequisicionAutorizadaIntegrationEvent"/>. Lo invoca
/// MediatR cuando el handler de Autorizar publica el domain event
/// (antes del SaveChanges); el mapper encolas al
/// <see cref="IIntegrationEventBuffer"/>; el
/// <c>OutboxSaveChangesInterceptor</c> lo persiste en
/// <c>compras.integration_events_outbox</c> dentro de la misma TX.
/// </summary>
public sealed class RequisicionAutorizadaMapper : INotificationHandler<MatrizAprobacionSatisfechaEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public RequisicionAutorizadaMapper(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task Handle(MatrizAprobacionSatisfechaEvent notification, CancellationToken cancellationToken)
    {
        var integration = new RequisicionAutorizadaIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            RequisicionId: notification.RequisicionId);
        return _publisher.PublishAsync(integration, cancellationToken);
    }
}
