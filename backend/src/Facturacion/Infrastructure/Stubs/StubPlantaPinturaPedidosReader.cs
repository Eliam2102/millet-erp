using Microsoft.Extensions.Logging;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.Stubs;

/// <summary>
/// Stub del lector de pedidos de Planta Pintura (F10-PR2). Devuelve vacío hasta
/// que <c>Integraciones.Origenes</c> exponga las órdenes vía Hybrid Connection.
/// PLATFORM-TODO(&lt;PlantaPinturaOrigenes&gt;).
/// </summary>
public sealed class StubPlantaPinturaPedidosReader : IPlantaPinturaPedidosReader
{
    private readonly ILogger<StubPlantaPinturaPedidosReader> _logger;

    public StubPlantaPinturaPedidosReader(ILogger<StubPlantaPinturaPedidosReader> logger) => _logger = logger;

    public Task<IReadOnlyList<PedidoPlantaPintura>> LeerPendientesAsync(int max, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[StubPlantaPinturaPedidosReader] Sin Integraciones.Origenes en dev — 0 pedidos. PLATFORM-TODO(<PlantaPinturaOrigenes>).");
        return Task.FromResult<IReadOnlyList<PedidoPlantaPintura>>([]);
    }
}
