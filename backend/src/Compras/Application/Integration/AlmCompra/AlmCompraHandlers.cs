using MediatR;
using Millet.Compras.Domain.Oc.Events;

namespace Millet.Compras.Application.Integration.AlmCompra;

// Disparador en proceso (antes del SaveChanges del llamador, igual que los mappers de eventos de integración): la fila de la
// bandeja queda en la misma transacción que la autorización. El pedimento se engancha directo en ActualizarNumeroPedimentoHandler
// (la OC no emite evento al editarlo) y la cancelación en los handlers de cancelar: no es automática, depende del acuse.

public sealed class AlmCompraOcAutorizadaHandler(AlmCompraEncolador encolador) : INotificationHandler<OrdenCompraAutorizadaEvent>
{
    public Task Handle(OrdenCompraAutorizadaEvent notification, CancellationToken cancellationToken) =>
        encolador.OcAutorizadaAsync(notification.OrdenCompraId, cancellationToken);
}
