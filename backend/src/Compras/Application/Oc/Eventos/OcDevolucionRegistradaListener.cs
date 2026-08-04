using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Oc.Events;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.Eventos;

/// <summary>
/// Listener in-proc (F5-PR2) que consume una devolución de material y
/// ajusta el acumulado recibido. Si la OC estaba <c>Cerrada</c> y el
/// recálculo deja alguna dimensión fuera del cierre, el agregado
/// transiciona de regreso a <c>Autorizada</c> y emite
/// <see cref="OrdenCompraReabriertaEvent"/>.
/// </summary>
public sealed class OcDevolucionRegistradaListener
    : INotificationHandler<OcDevolucionRegistradaEvent>
{
    private readonly ComprasDbContext _db;
    private readonly IPublisher _publisher;
    private readonly ILogger<OcDevolucionRegistradaListener> _logger;

    public OcDevolucionRegistradaListener(
        ComprasDbContext db,
        IPublisher publisher,
        ILogger<OcDevolucionRegistradaListener> logger)
    {
        _db = db;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Handle(OcDevolucionRegistradaEvent notification, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .Include(o => o.Lineas)
            .FirstOrDefaultAsync(o => o.Id == notification.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró OC '{notification.OrdenCompraId}' al registrar devolución.");

        var resultado = oc.RegistrarRecepcionLinea(
            notification.LineaOrdenCompraId,
            notification.CantidadAcumuladaAjustada,
            notification.OcurridoEn);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Devolución aplicada. OcId={OcId} LineaId={LineaId} CantidadAjustada={Cantidad} Reabierta={Reabierta}",
            notification.OrdenCompraId,
            notification.LineaOrdenCompraId,
            notification.CantidadAcumuladaAjustada,
            resultado.Reabrierta is not null);

        if (resultado.Reabrierta is { } reabierta)
        {
            await _publisher.Publish(reabierta, cancellationToken);
        }
    }
}
