using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Ports.Cxp;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.Eventos;

/// <summary>
/// Listener in-proc (F5-PR2) que consume eventos de nota de crédito y
/// reduce el acumulado facturado de la línea. Si la OC estaba
/// <c>Cerrada</c>, el ajuste puede dispararla de regreso a Autorizada
/// vía el recálculo del agregado.
/// </summary>
public sealed class NotaCreditoProveedorRegistradaListener
    : INotificationHandler<NotaCreditoProveedorRegistradaEvent>
{
    private readonly ComprasDbContext _db;
    private readonly IPublisher _publisher;
    private readonly ILogger<NotaCreditoProveedorRegistradaListener> _logger;

    public NotaCreditoProveedorRegistradaListener(
        ComprasDbContext db,
        IPublisher publisher,
        ILogger<NotaCreditoProveedorRegistradaListener> logger)
    {
        _db = db;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Handle(NotaCreditoProveedorRegistradaEvent notification, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .Include(o => o.Lineas)
            .FirstOrDefaultAsync(o => o.Id == notification.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró OC '{notification.OrdenCompraId}' al registrar nota de crédito.");

        var resultado = oc.RegistrarFacturacionLinea(
            notification.LineaOrdenCompraId,
            notification.CantidadFacturadaAcumuladaAjustada,
            notification.OcurridoEn);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Nota de crédito aplicada. OcId={OcId} LineaId={LineaId} CantidadAjustada={Cantidad} Reabierta={Reabierta}",
            notification.OrdenCompraId,
            notification.LineaOrdenCompraId,
            notification.CantidadFacturadaAcumuladaAjustada,
            resultado.Reabrierta is not null);

        if (resultado.Reabrierta is { } reabierta)
        {
            await _publisher.Publish(reabierta, cancellationToken);
        }
    }
}
