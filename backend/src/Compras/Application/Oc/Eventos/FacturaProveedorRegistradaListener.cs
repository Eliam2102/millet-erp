using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Ports.Cxp;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.Eventos;

/// <summary>
/// Listener in-proc (F5-PR2) que consume eventos de factura proveedor
/// registrados en CxP y actualiza el acumulado facturado de la línea
/// vía <c>OrdenCompra.RegistrarFacturacionLinea</c>.
/// </summary>
public sealed class FacturaProveedorRegistradaListener
    : INotificationHandler<FacturaProveedorRegistradaEvent>
{
    private readonly ComprasDbContext _db;
    private readonly IPublisher _publisher;
    private readonly ILogger<FacturaProveedorRegistradaListener> _logger;

    public FacturaProveedorRegistradaListener(
        ComprasDbContext db,
        IPublisher publisher,
        ILogger<FacturaProveedorRegistradaListener> logger)
    {
        _db = db;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Handle(FacturaProveedorRegistradaEvent notification, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .Include(o => o.Lineas)
            .FirstOrDefaultAsync(o => o.Id == notification.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró OC '{notification.OrdenCompraId}' al registrar factura.");

        var resultado = oc.RegistrarFacturacionLinea(
            notification.LineaOrdenCompraId,
            notification.CantidadFacturadaAcumulada,
            notification.OcurridoEn);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Factura aplicada. OcId={OcId} LineaId={LineaId} CantidadAcumulada={Cantidad}",
            notification.OrdenCompraId,
            notification.LineaOrdenCompraId,
            notification.CantidadFacturadaAcumulada);

        if (resultado.Cerrada is { } cerrada)
        {
            await _publisher.Publish(cerrada, cancellationToken);
        }
    }
}
