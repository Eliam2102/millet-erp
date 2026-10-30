using MediatR;
using Millet.Compras.Domain.Oc.Events;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Integration.Oc.Mappers;

public sealed class OcServicioConfirmadoMapper(IIntegrationEventPublisher publisher)
    : INotificationHandler<OrdenCompraServicioConfirmadoEvent>
{
    public Task Handle(OrdenCompraServicioConfirmadoEvent n, CancellationToken cancellationToken) =>
        publisher.PublishAsync(new OcServicioConfirmadoIntegrationEvent(
            n.EmpresaId, n.OcurridoEn, n.OrdenCompraId, n.Folio, n.Confirmado, n.UsuarioId, n.Texto), cancellationToken);
}
