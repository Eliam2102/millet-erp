using Microsoft.Extensions.Logging;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.Stubs;

/// <summary>
/// FAC-08: stub del lector de salidas parciales. Devuelve vacío hasta que Producto terminado
/// (PT-01/PT-03) y Comercio Exterior (CE-02) publiquen sus salidas. PLATFORM-TODO(&lt;SalidasPt&gt;).
/// </summary>
public sealed class StubSalidasParcialesReader(ILogger<StubSalidasParcialesReader> logger) : ISalidasParcialesReader
{
    // PLATFORM-TODO(<SalidasPt>): leer las salidas de PT-03 y los contenedores de CE-02.
    public Task<IReadOnlyList<SalidaParcial>> LeerPendientesAsync(int max, CancellationToken cancellationToken)
    {
        logger.LogDebug("[StubSalidasParcialesReader] Sin PT ni CE — 0 salidas. PLATFORM-TODO(<SalidasPt>).");
        return Task.FromResult<IReadOnlyList<SalidaParcial>>([]);
    }
}
