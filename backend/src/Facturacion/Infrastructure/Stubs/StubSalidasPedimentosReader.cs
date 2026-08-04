using Microsoft.Extensions.Logging;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.Stubs;

/// <summary>
/// Stub del lector de pedimentos del Sistema de Salidas (F7-PR2). Devuelve vacío
/// hasta que <c>Integraciones.Origenes</c> exponga las Hojas de Salida vía Hybrid
/// Connection. PLATFORM-TODO(&lt;SalidasPedimentos&gt;).
/// </summary>
public sealed class StubSalidasPedimentosReader : ISalidasPedimentosReader
{
    private readonly ILogger<StubSalidasPedimentosReader> _logger;

    public StubSalidasPedimentosReader(ILogger<StubSalidasPedimentosReader> logger) => _logger = logger;

    public Task<IReadOnlyList<PedimentoSalida>> LeerPendientesAsync(int max, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[StubSalidasPedimentosReader] Sin Sistema de Salidas en dev — 0 pedimentos. PLATFORM-TODO(<SalidasPedimentos>).");
        return Task.FromResult<IReadOnlyList<PedimentoSalida>>([]);
    }
}
