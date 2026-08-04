using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;

namespace Millet.CuentasPorPagar.Infrastructure.Stubs;

/// <summary>
/// Stub de <see cref="IProveedorReadPort"/> (F0-PR1). Devuelve <c>null</c>
/// — los handlers degradan a tolerancia default global del módulo.
///
/// PLATFORM-TODO(&lt;ProveedorReadPort&gt;): adapter contra
/// <c>datos_maestros.proveedor</c> cuando se extiende el master con
/// los atributos operativos de CxP (tolerancia, en_revision).
/// </summary>
public sealed class NoOpProveedorReadPort : IProveedorReadPort
{
    private readonly ILogger<NoOpProveedorReadPort> _logger;

    public NoOpProveedorReadPort(ILogger<NoOpProveedorReadPort> logger)
    {
        _logger = logger;
    }

    public Task<ProveedorDto?> ObtenerAsync(Guid proveedorId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOp] IProveedorReadPort.ObtenerAsync proveedorId={ProveedorId}", proveedorId);
        return Task.FromResult<ProveedorDto?>(null);
    }

    public Task<ProveedorDto?> ObtenerPorRfcAsync(string rfc, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOp] IProveedorReadPort.ObtenerPorRfcAsync rfc=***");
        return Task.FromResult<ProveedorDto?>(null);
    }

    public Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresPorIdsAsync(
        IReadOnlyCollection<Guid> proveedorIds,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "[NoOp] IProveedorReadPort.ObtenerNombresPorIdsAsync count={Count}",
            proveedorIds.Count);
        return Task.FromResult<IReadOnlyDictionary<Guid, string>>(
            new Dictionary<Guid, string>());
    }
}
