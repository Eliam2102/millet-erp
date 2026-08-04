using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Ports.Tesoreria;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.Eventos;

/// <summary>
/// Listener in-proc (F5-PR2) que consume eventos de pago desde Tesorería
/// y actualiza <c>OrdenCompra.MontoPagado</c> vía
/// <c>RegistrarPago</c>. Si el pago completa el total y las otras 2
/// dimensiones están cerradas, dispara <c>OrdenCompraCerradaEvent</c>.
/// </summary>
public sealed class PagoFacturaProveedorListener
    : INotificationHandler<PagoFacturaProveedorEvent>
{
    private readonly ComprasDbContext _db;
    private readonly IPublisher _publisher;
    private readonly ILogger<PagoFacturaProveedorListener> _logger;

    public PagoFacturaProveedorListener(
        ComprasDbContext db,
        IPublisher publisher,
        ILogger<PagoFacturaProveedorListener> logger)
    {
        _db = db;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Handle(PagoFacturaProveedorEvent notification, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .Include(o => o.Lineas)
            .FirstOrDefaultAsync(o => o.Id == notification.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró OC '{notification.OrdenCompraId}' al registrar pago.");

        var resultado = oc.RegistrarPago(
            notification.MontoPagadoAcumulado,
            notification.OcurridoEn);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Pago aplicado. OcId={OcId} MontoAcumulado={Monto} Cerrada={Cerrada} Reabierta={Reabierta}",
            notification.OrdenCompraId,
            notification.MontoPagadoAcumulado,
            resultado.Cerrada is not null,
            resultado.Reabrierta is not null);

        if (resultado.Cerrada is { } cerrada)
        {
            await _publisher.Publish(cerrada, cancellationToken);
        }
        if (resultado.Reabrierta is { } reabierta)
        {
            await _publisher.Publish(reabierta, cancellationToken);
        }
    }
}
