using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;

namespace Millet.CuentasPorPagar.Infrastructure.Stubs;

/// <summary>
/// Stub de <see cref="IArticuloReadPort"/> (F0-PR1). Devuelve <c>null</c>.
/// PLATFORM-TODO(&lt;ArticuloReadPort&gt;): adapter real cuando se conecte
/// a <c>datos_maestros.articulo</c>.
/// </summary>
public sealed class NoOpArticuloReadPort : IArticuloReadPort
{
    private readonly ILogger<NoOpArticuloReadPort> _logger;

    public NoOpArticuloReadPort(ILogger<NoOpArticuloReadPort> logger)
    {
        _logger = logger;
    }

    public Task<ArticuloDto?> ObtenerAsync(Guid articuloId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOp] IArticuloReadPort.ObtenerAsync articuloId={ArticuloId}", articuloId);
        return Task.FromResult<ArticuloDto?>(null);
    }
}
