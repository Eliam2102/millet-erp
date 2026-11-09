using MediatR;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Oc.Events;
using Millet.CuentasPorPagar.Application.Cfdi.SimularCfdi;

namespace Millet.CuentasPorPagar.Application.EventListeners;

/// <summary>
/// Listener in-process que captura la notificación de orden de compra enviada al proveedor
/// para disparar de forma automática e idempotente la simulación del CFDI 4.0 sintético de prueba
/// en entornos fuera de producción.
/// Cualquier error durante la generación es capturado y logueado sin interrumpir el flujo de Compras.
/// </summary>
public sealed class OrdenCompraEnviadaAlProveedorListener(
    ISender sender,
    ILogger<OrdenCompraEnviadaAlProveedorListener> logger)
    : INotificationHandler<OrdenCompraEnviadaAlProveedorNotification>
{
    public async Task Handle(OrdenCompraEnviadaAlProveedorNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Disparando generación automática de CFDI de prueba para OC {OrdenCompraId} ({Folio})...",
                notification.OrdenCompraId, notification.Folio);
            await sender.Send(new GenerarEIngresarCfdiPruebaCommand(notification.OrdenCompraId), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al generar automáticamente el CFDI de prueba para la OC {OrdenCompraId}.",
                notification.OrdenCompraId);
        }
    }
}
