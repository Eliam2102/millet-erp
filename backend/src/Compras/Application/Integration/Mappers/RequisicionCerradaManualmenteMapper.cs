using MediatR;
using Millet.Compras.Domain.Events;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Integration.Mappers;

public sealed class RequisicionCerradaManualmenteMapper
    : INotificationHandler<RequisicionCerradaManualmenteEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public RequisicionCerradaManualmenteMapper(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task Handle(RequisicionCerradaManualmenteEvent notification, CancellationToken cancellationToken)
    {
        var integration = new RequisicionCerradaManualmenteIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            RequisicionId: notification.RequisicionId,
            EstadoFinal: notification.EstadoFinal,
            MotivoId: notification.MotivoId,
            MotivoTexto: notification.MotivoTexto,
            ActorId: notification.ActorId);
        return _publisher.PublishAsync(integration, cancellationToken);
    }
}
