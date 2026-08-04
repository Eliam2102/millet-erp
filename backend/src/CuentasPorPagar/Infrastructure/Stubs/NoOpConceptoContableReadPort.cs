using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Ports.Contabilidad;

namespace Millet.CuentasPorPagar.Infrastructure.Stubs;

/// <summary>
/// Stub de <see cref="IConceptoContableReadPort"/> (F0-PR1). Devuelve
/// listas vacías. PLATFORM-TODO(&lt;ContabilidadConceptos&gt;): adapter
/// real cuando Contabilidad entre en runtime.
/// </summary>
public sealed class NoOpConceptoContableReadPort : IConceptoContableReadPort
{
    private readonly ILogger<NoOpConceptoContableReadPort> _logger;

    public NoOpConceptoContableReadPort(ILogger<NoOpConceptoContableReadPort> logger)
    {
        _logger = logger;
    }

    public Task<ConceptoContableDto?> ObtenerAsync(Guid conceptoId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOp] IConceptoContableReadPort.ObtenerAsync conceptoId={ConceptoId}", conceptoId);
        return Task.FromResult<ConceptoContableDto?>(null);
    }

    public Task<IReadOnlyList<ConceptoContableDto>> ListarAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<ConceptoContableDto>>([]);
    }
}
