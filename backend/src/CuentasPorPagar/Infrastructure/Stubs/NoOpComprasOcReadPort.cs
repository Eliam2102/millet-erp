using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Ports.Compras;

namespace Millet.CuentasPorPagar.Infrastructure.Stubs;

/// <summary>
/// Stub de <see cref="IComprasOcReadPort"/> (F0-PR1). Devuelve <c>null</c>
/// y listas vacías hasta que F5-PR1 cablée el adapter real contra el
/// módulo Compras (que ya está en runtime — el stub es solo el andamio
/// del F0).
/// </summary>
public sealed class NoOpComprasOcReadPort : IComprasOcReadPort
{
    private readonly ILogger<NoOpComprasOcReadPort> _logger;

    public NoOpComprasOcReadPort(ILogger<NoOpComprasOcReadPort> logger)
    {
        _logger = logger;
    }

    public Task<OrdenCompraDto?> ObtenerAsync(Guid ordenCompraId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOp] IComprasOcReadPort.ObtenerAsync ocId={OcId}", ordenCompraId);
        return Task.FromResult<OrdenCompraDto?>(null);
    }

    public Task<IReadOnlyList<OrdenCompraDto>> ListarAutorizadasPorProveedorAsync(
        Guid proveedorId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOp] IComprasOcReadPort.ListarAutorizadasPorProveedorAsync proveedorId={ProveedorId}", proveedorId);
        return Task.FromResult<IReadOnlyList<OrdenCompraDto>>([]);
    }
}
