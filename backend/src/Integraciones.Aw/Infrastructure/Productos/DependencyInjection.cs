using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Infrastructure.Clientes;
using Millet.Integraciones.Aw.Infrastructure.OrigenPg;

namespace Millet.Integraciones.Aw.Infrastructure.Productos;

/// <summary>
/// Wiring de la sincronización de productos A+W (ADM-07). Apagado por defecto: el origen
/// simulado solo se registra con <c>OrigenHabilitado=true</c> y <c>ArchivoSimulado</c> explícitos.
/// </summary>
public static class ProductosDependencyInjection
{
    public static IServiceCollection AddIntegracionesAwProductos(
        this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AwProductosOptions.SectionName);
        services.AddOptions<AwProductosOptions>().Bind(section);
        var opciones = section.Get<AwProductosOptions>() ?? new AwProductosOptions();

        // PLATFORM-TODO(<AwProductosHybridConnection>): lector SQL construido y apagado por defecto; falta
        // O1A-AW-INT (A3: ruta privada + TLS válido) para habilitarlo contra A+W.
        if (opciones.OrigenHabilitado)
        {
            if (opciones.Origen == AwProductosOrigenTipo.Sql)
            {
                // Solo con connection string real (una ref KV sin resolver se trata como ausente).
                var cs = configuration.GetConnectionString(AwProductosOptions.ConnectionStringName);
                if (!string.IsNullOrWhiteSpace(cs)
                    && !cs.StartsWith("@Microsoft.KeyVault", StringComparison.OrdinalIgnoreCase))
                {
                    var factory = new ClientesDependencyInjection.ClientesSqlConnectionFactory(
                        cs, AwProductosOptions.ConnectionStringName);
                    services.AddSingleton<IAwProductosOrigen>(sp => new AwProductosSqlOrigen(
                        factory,
                        sp.GetRequiredService<IOptions<AwProductosOptions>>(),
                        sp.GetRequiredService<ILogger<AwProductosSqlOrigen>>()));
                }
            }
            else if (opciones.Origen == AwProductosOrigenTipo.Postgres)
            {
                var cs = configuration.GetConnectionString(AwOrigenPg.ConnectionStringName);
                if (!string.IsNullOrWhiteSpace(cs))
                    services.AddSingleton<IAwProductosOrigen>(sp => new AwProductosPgOrigen(
                        new AwOrigenPg.Fabrica(cs),
                        sp.GetRequiredService<IOptions<AwProductosOptions>>(),
                        sp.GetRequiredService<ILogger<AwProductosPgOrigen>>()));
            }
            else if (!string.IsNullOrWhiteSpace(opciones.ArchivoSimulado))
                services.AddSingleton<IAwProductosOrigen>(_ => AwProductosOrigenSimulado.DesdeArchivo(opciones.ArchivoSimulado!));
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AwProductosSincronizador>();
        return services;
    }
}
