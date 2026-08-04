namespace Millet.SharedKernel.Infrastructure.HealthChecks;

/// <summary>
/// Lista de tipos de DbContext que el <see cref="MigrationsAppliedHealthCheck"/>
/// debe consultar. La poblamos desde Program.cs (único proyecto con
/// referencia a todos los módulos) en lugar de hard-codear los tipos en
/// SharedKernel — eso forzaría a SharedKernel a referenciar cada módulo
/// e invertiría la dependencia (los módulos dependen de SharedKernel,
/// no al revés).
/// </summary>
public sealed class MigrationsHealthCheckOptions
{
    public List<Type> ContextTypes { get; init; } = [];
}
