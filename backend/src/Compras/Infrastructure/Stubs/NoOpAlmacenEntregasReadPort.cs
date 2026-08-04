using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Ports.Almacen;

namespace Millet.Compras.Infrastructure.Stubs;

/// <summary>
/// Stub NoOp de <see cref="IAlmacenEntregasReadPort"/>. Devuelve
/// siempre diccionario vacío. Útil para tests unitarios y para
/// arrancar el API sin el adapter real registrado (raro — en runtime
/// el adapter está override).
///
/// PLATFORM-TODO(&lt;StubsTeardown&gt;): borrar cuando el adapter
/// productivo sea la única registración.
/// </summary>
public sealed class NoOpAlmacenEntregasReadPort : IAlmacenEntregasReadPort
{
    private static readonly IReadOnlyDictionary<Guid, EntregaReclasificacion> Empty =
        new Dictionary<Guid, EntregaReclasificacion>();

    private readonly ILogger<NoOpAlmacenEntregasReadPort> _logger;

    public NoOpAlmacenEntregasReadPort(ILogger<NoOpAlmacenEntregasReadPort> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyDictionary<Guid, EntregaReclasificacion>> ObtenerEntregaParaReclasificacionAsync(
        IReadOnlyCollection<Guid> requisicionIds,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "[NoOpAlmacenEntregasReadPort] reclasif rqs={Count} → empty (stub)",
            requisicionIds.Count);
        return Task.FromResult(Empty);
    }
}
