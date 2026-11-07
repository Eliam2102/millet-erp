using Microsoft.Extensions.Logging;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.Stubs;

/// <summary>
/// FAC-08: stub del lector de salidas parciales de Producto terminado (PT-01/PT-03). Devuelve vacío
/// hasta que PT publique su salida con albarán (PT5.5, Fase 2). La fuente de Comercio Exterior ya está
/// conectada por evento: <c>comercio_exterior.contenedor.embarcado.v1</c> llega al
/// <c>ComercioExteriorEventListenerWorker</c> de Facturación y registra una salida por pedido del
/// contenedor; no pasa por este lector. PLATFORM-TODO(&lt;SalidasPt&gt;).
/// </summary>
public sealed class StubSalidasParcialesReader(ILogger<StubSalidasParcialesReader> logger) : ISalidasParcialesReader
{
    // PLATFORM-TODO(<SalidasPt>): leer las salidas de PT-03 (albarán). Los contenedores de CE ya llegan por evento (FAC-08 Fase 1).
    public Task<IReadOnlyList<SalidaParcial>> LeerPendientesAsync(int max, CancellationToken cancellationToken)
    {
        logger.LogDebug("[StubSalidasParcialesReader] Sin salidas de PT — 0 salidas (CE llega por evento). PLATFORM-TODO(<SalidasPt>).");
        return Task.FromResult<IReadOnlyList<SalidaParcial>>([]);
    }
}
