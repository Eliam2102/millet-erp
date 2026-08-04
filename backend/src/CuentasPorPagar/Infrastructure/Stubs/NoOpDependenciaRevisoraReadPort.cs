using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;

namespace Millet.CuentasPorPagar.Infrastructure.Stubs;

/// <summary>
/// Stub de <see cref="IDependenciaRevisoraReadPort"/> (F0-PR1, A21 del
/// 01-diseno §3) con seed local de las 4 dependencias revisoras más
/// comunes. Permite que F4-PR1 (workflow de revisión) arranque sin
/// bloqueo por catálogo externo.
///
/// PLATFORM-TODO(&lt;DependenciasRevisorasEnAdmin&gt;): reemplazar por
/// adapter contra el catálogo compartido cuando Administración lo
/// exponga.
/// </summary>
public sealed class NoOpDependenciaRevisoraReadPort : IDependenciaRevisoraReadPort
{
    private static readonly IReadOnlyList<DependenciaRevisoraDto> SeedLocal =
    [
        new(Guid.Parse("11111111-0000-0000-0000-000000000001"), "COMPRAS",     "Compras",            Activa: true),
        new(Guid.Parse("11111111-0000-0000-0000-000000000002"), "ALMACEN",     "Almacén",            Activa: true),
        new(Guid.Parse("11111111-0000-0000-0000-000000000003"), "PRODUCCION",  "Producción",         Activa: true),
        new(Guid.Parse("11111111-0000-0000-0000-000000000004"), "DIRECCION_F", "Dirección Finanzas", Activa: true),
    ];

    private readonly ILogger<NoOpDependenciaRevisoraReadPort> _logger;

    public NoOpDependenciaRevisoraReadPort(ILogger<NoOpDependenciaRevisoraReadPort> logger)
    {
        _logger = logger;
    }

    public Task<DependenciaRevisoraDto?> ObtenerAsync(Guid dependenciaId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOp] IDependenciaRevisoraReadPort.ObtenerAsync id={Id}", dependenciaId);
        var match = SeedLocal.FirstOrDefault(d => d.Id == dependenciaId);
        return Task.FromResult<DependenciaRevisoraDto?>(match);
    }

    public Task<IReadOnlyList<DependenciaRevisoraDto>> ListarAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(SeedLocal);
    }
}
