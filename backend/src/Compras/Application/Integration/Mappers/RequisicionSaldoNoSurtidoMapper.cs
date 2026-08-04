using MediatR;
using Millet.Compras.Domain.Events;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Integration.Mappers;

public sealed class RequisicionSaldoNoSurtidoMapper : INotificationHandler<SaldoNoSurtidoEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public RequisicionSaldoNoSurtidoMapper(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task Handle(SaldoNoSurtidoEvent notification, CancellationToken cancellationToken)
    {
        var integration = new RequisicionSaldoNoSurtidoIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            RequisicionId: notification.RequisicionId,
            LineaRequisicionId: notification.LineaRequisicionId,
            OrdenCompraId: notification.OrdenCompraId,
            CantidadSolicitada: notification.CantidadSolicitada,
            CantidadEntregada: notification.CantidadEntregada,
            Saldo: notification.Saldo);
        return _publisher.PublishAsync(integration, cancellationToken);
    }
}
