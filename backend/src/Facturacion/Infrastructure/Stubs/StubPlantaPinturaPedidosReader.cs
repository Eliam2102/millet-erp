using Microsoft.Extensions.Logging;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.Stubs;

/// <summary>
/// Fallback dev del lector de pedidos de Planta Pintura (F10-PR2): devuelve vacío.
/// Con <c>ConnectionStrings:AwIntegracionDb</c> lo sustituye <c>PinturasPedidosAdapter</c>
/// (Integraciones.Aw, I13.9) sobre <c>dbo.pinturas_pedido</c>.
/// </summary>
public sealed class StubPlantaPinturaPedidosReader : IPlantaPinturaPedidosReader
{
    private readonly ILogger<StubPlantaPinturaPedidosReader> _logger;

    public StubPlantaPinturaPedidosReader(ILogger<StubPlantaPinturaPedidosReader> logger) => _logger = logger;

    public Task<IReadOnlyList<PedidoPlantaPintura>> LeerPendientesAsync(int max, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[StubPlantaPinturaPedidosReader] Sin ConnectionStrings:AwIntegracionDb — 0 pedidos de Planta Pintura.");
        return Task.FromResult<IReadOnlyList<PedidoPlantaPintura>>([]);
    }

    public Task MarcarResultadoAsync(
        string numeroPedido, ResultadoAcusePlantaPintura resultado, Guid? pedidoId, string? detalleError,
        DateTimeOffset fecha, CancellationToken cancellationToken) => Task.CompletedTask;
}
