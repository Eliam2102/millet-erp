using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorCobrar.Infrastructure.Adapters;

namespace Millet.CuentasPorCobrar.Infrastructure;

/// <summary>
/// Wiring de DI del módulo Cuentas por Cobrar (CXC-PR1/PR3).
///
/// <para>
/// El DbContext se registra en <c>Program.cs</c> de Api (junto con los
/// demás módulos) para compartir el wiring de interceptors transversales
/// y la connection string. El <c>FacturacionEventListenerWorker</c>
/// también se registra en Program.cs, dentro del bloque condicionado a
/// que exista la conexión de Service Bus (mismo patrón que los listeners
/// de la triada). El adapter de <c>IFacturacionAnticiposReadPort</c> lo
/// registra Facturación (módulo dueño del dato, §6.3 del 01-diseño).
/// </para>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCuentasPorCobrarModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // CXC-PR3: resolución RFC → cliente para la proyección de cartera.
        services.AddScoped<IClienteReadPort, ClienteReadPortAdapter>();

        // CXC-PR6: buckets de antigüedad configurables (levantamiento §1.2).
        services
            .AddOptions<Application.Reportes.Comun.ReportesCxcOptions>()
            .Bind(configuration.GetSection(Application.Reportes.Comun.ReportesCxcOptions.SectionName));
        services.AddSingleton<Application.Reportes.Comun.BucketsAntiguedadCxc>();

        // CXC-PR7: tolerancias no fiscales por moneda (default MXN provisional
        // hasta que fiscal cierre el gate <$50 USD sin CFDI).
        services
            .AddOptions<Application.AplicacionPagos.AplicacionPagosOptions>()
            .Bind(configuration.GetSection(Application.AplicacionPagos.AplicacionPagosOptions.SectionName));

        // CXC-PR8: worker diario de alertas de cartera (SOLUNION 90d, exceso
        // de crédito, auto-bloqueo por vencimientos).
        services
            .AddOptions<Application.Alertas.AlertasCarteraOptions>()
            .Bind(configuration.GetSection(Application.Alertas.AlertasCarteraOptions.SectionName));
        services.AddHostedService<Workers.AlertaCarteraWorker>();

        return services;
    }
}
