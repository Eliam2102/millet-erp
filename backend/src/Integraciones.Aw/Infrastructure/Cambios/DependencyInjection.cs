using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Cambios;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Workers;
using Millet.Integraciones.Aw.Infrastructure.Clientes;

namespace Millet.Integraciones.Aw.Infrastructure.Cambios;

/// <summary>
/// Wiring de la sincronización por CDC. Con <c>Habilitado=false</c> (default) no registra nada.
/// Habilitado exige <c>ConnectionStrings:AwCambiosDb</c> real (TLS verificado, usuario de solo lectura con acceso a <c>cdc</c>).
/// </summary>
public static class CambiosDependencyInjection
{
    public static IServiceCollection AddIntegracionesAwCambios(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AwCambiosOptions.SectionName);
        services.AddOptions<AwCambiosOptions>().Bind(section);
        var o = section.Get<AwCambiosOptions>() ?? new AwCambiosOptions();
        if (!o.Habilitado) return services;

        var cs = configuration.GetConnectionString(AwCambiosOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(cs) || cs.StartsWith("@Microsoft.KeyVault", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{AwCambiosOptions.SectionName}:Habilitado requiere ConnectionStrings:{AwCambiosOptions.ConnectionStringName}.");

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAwCambiosOrigen>(sp => new AwCambiosCdcOrigen(
            new ClientesDependencyInjection.ClientesSqlConnectionFactory(cs, AwCambiosOptions.ConnectionStringName),
            new AwPedidosOptions { SqlConnectTimeoutSeconds = o.SqlConnectTimeoutSeconds, SqlQueryTimeoutSeconds = o.SqlQueryTimeoutSeconds },
            sp.GetRequiredService<ILogger<AwCambiosCdcOrigen>>()));
        services.AddScoped<AwCambiosAplicador>();
        services.AddSingleton<AwCambiosSyncWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<AwCambiosSyncWorker>());
        services.AddHealthChecks().AddCheck<AwCambiosSyncWorkerHealthCheck>("aw-cambios-sync-worker", tags: ["liveness"]);
        return services;
    }
}
