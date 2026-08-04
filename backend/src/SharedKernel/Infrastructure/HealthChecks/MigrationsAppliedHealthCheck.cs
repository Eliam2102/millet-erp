using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Millet.SharedKernel.Infrastructure.HealthChecks;

/// <summary>
/// Verifica que todas las migraciones esperadas estén aplicadas en los
/// DbContexts registrados. Si hay migraciones pendientes en cualquiera,
/// reporta <see cref="HealthStatus.Unhealthy"/> con detalle. Útil porque
/// si la app arranca antes de que el job de migraciones termine,
/// <c>/health/ready</c> responde 503 hasta que la BD está sincronizada
/// con el modelo. Ver ADR-0019.
///
/// La lista de tipos a chequear se configura desde Program.cs vía
/// <see cref="MigrationsHealthCheckOptions"/>. SharedKernel no conoce
/// los DbContext de los módulos, así que es responsabilidad del host
/// (Api) declarar qué chequear.
/// </summary>
public sealed class MigrationsAppliedHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _services;
    private readonly MigrationsHealthCheckOptions _options;

    public MigrationsAppliedHealthCheck(IServiceProvider services, MigrationsHealthCheckOptions options)
    {
        _services = services;
        _options = options;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var pendingByContext = new Dictionary<string, IReadOnlyCollection<string>>();

        using var scope = _services.CreateScope();

        foreach (var contextType in _options.ContextTypes)
        {
            if (scope.ServiceProvider.GetService(contextType) is not DbContext db)
            {
                continue;
            }

            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count > 0)
            {
                pendingByContext[contextType.Name] = pending;
            }
        }

        if (pendingByContext.Count == 0)
        {
            return HealthCheckResult.Healthy("Todas las migraciones aplicadas.");
        }

        var description = string.Join("; ",
            pendingByContext.Select(kv => $"{kv.Key} ({kv.Value.Count}): {string.Join(", ", kv.Value)}"));

        return HealthCheckResult.Unhealthy($"Migraciones pendientes — {description}");
    }
}
