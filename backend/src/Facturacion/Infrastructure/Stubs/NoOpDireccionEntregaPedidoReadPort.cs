using Microsoft.Extensions.Logging;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.Stubs;

/// <summary>
/// M-354: stub de la dirección de entrega del pedido. Devuelve <c>null</c> hasta que Integraciones ajuste la vista del ERP con
/// las direcciones de <c>BW_AUFTR_KOPF</c> (principal <c>AH_*</c> y de entrega <c>AL_*</c>); mientras tanto el destino del
/// tramo se captura a mano.
/// PLATFORM-TODO(&lt;DireccionEntregaPedidoAw&gt;).
/// </summary>
public sealed class NoOpDireccionEntregaPedidoReadPort(ILogger<NoOpDireccionEntregaPedidoReadPort> logger) : IDireccionEntregaPedidoReadPort
{
    public Task<DireccionesPedidoAw?> ObtenerAsync(Guid pedidoFacturableId, CancellationToken cancellationToken)
    {
        logger.LogDebug("[NoOpDireccionEntregaPedidoReadPort] Sin vista de direcciones A+W — pedido {Pedido}. PLATFORM-TODO(<DireccionEntregaPedidoAw>).", pedidoFacturableId);
        return Task.FromResult<DireccionesPedidoAw?>(null);
    }
}
