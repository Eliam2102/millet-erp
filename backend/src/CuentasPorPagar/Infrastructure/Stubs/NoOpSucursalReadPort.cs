using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;

namespace Millet.CuentasPorPagar.Infrastructure.Stubs;

/// <summary>
/// Stub de <see cref="ISucursalReadPort"/> (F0-PR1). Devuelve listas
/// vacías y <c>null</c> en lookups. Adapter real cuando el módulo
/// Administración exponga el catálogo cross-módulo.
///
/// PLATFORM-TODO(&lt;SucursalReadPort&gt;): reemplazar por adapter que
/// proyecte desde <c>administracion.sucursal</c>.
/// </summary>
public sealed class NoOpSucursalReadPort : ISucursalReadPort
{
    private readonly ILogger<NoOpSucursalReadPort> _logger;

    public NoOpSucursalReadPort(ILogger<NoOpSucursalReadPort> logger)
    {
        _logger = logger;
    }

    public Task<SucursalDto?> ObtenerAsync(Guid sucursalId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOp] ISucursalReadPort.ObtenerAsync sucursalId={SucursalId}", sucursalId);
        return Task.FromResult<SucursalDto?>(null);
    }

    public Task<IReadOnlyList<SucursalDto>> ListarPorEmpresaAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOp] ISucursalReadPort.ListarPorEmpresaAsync empresaId={EmpresaId}", empresaId);
        return Task.FromResult<IReadOnlyList<SucursalDto>>([]);
    }
}
