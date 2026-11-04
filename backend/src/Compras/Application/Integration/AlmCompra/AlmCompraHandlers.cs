using MediatR;
using Millet.Compras.Domain.Oc.Events;

namespace Millet.Compras.Application.Integration.AlmCompra;

// Disparadores en proceso (antes del SaveChanges del llamador, igual que los mappers de eventos de integración):
// la fila de la bandeja queda en la misma transacción que el cambio de la OC. El pedimento se engancha directo en
// ActualizarNumeroPedimentoHandler porque la OC no emite evento al editarlo.

public sealed class AlmCompraOcAutorizadaHandler(AlmCompraEncolador encolador) : INotificationHandler<OrdenCompraAutorizadaEvent>
{
    public Task Handle(OrdenCompraAutorizadaEvent notification, CancellationToken cancellationToken) =>
        encolador.OcAutorizadaAsync(notification.OrdenCompraId, cancellationToken);
}

public sealed class AlmCompraOcCanceladaHandler(AlmCompraEncolador encolador) : INotificationHandler<OrdenCompraCanceladaEvent>
{
    public Task Handle(OrdenCompraCanceladaEvent notification, CancellationToken cancellationToken) =>
        encolador.OcCanceladaAsync(notification.OrdenCompraId, notification.MotivoCancelacionTexto, cancellationToken);
}
