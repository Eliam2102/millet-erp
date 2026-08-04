using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Domain.Ports.OrdenCompra;
using Millet.Compras.Infrastructure.Oc.Ports;

namespace Millet.Compras.Infrastructure.Stubs;

/// <summary>
/// Helper de wiring para los stubs cross-module de Compras (F3-PR2).
///
/// <para>
/// <c>AddComprasStubs</c> bindea <see cref="ComprasStubsOptions"/> y, si
/// <c>Compras:UseStubs=true</c>, registra las 5 implementaciones
/// <c>InMemory*</c> de los puertos.
/// </para>
/// <para>
/// <b>Fail-loud guard</b>: si el flag está en <c>true</c> y el environment
/// es <c>Production</c>, el método lanza <see cref="InvalidOperationException"/>
/// para que el bootstrap del Api falle inmediatamente con un mensaje
/// explícito. Esto evita que un cambio de config involuntario meta los
/// stubs en producción.
/// </para>
/// </summary>
public static class ComprasStubsServiceCollectionExtensions
{
    public static IServiceCollection AddComprasStubs(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddOptions<ComprasStubsOptions>()
            .Bind(configuration.GetSection(ComprasStubsOptions.SectionName));

        var section = configuration.GetSection(ComprasStubsOptions.SectionName);
        var useStubs = section.GetValue<bool>(nameof(ComprasStubsOptions.UseStubs));

        if (!useStubs)
        {
            // F5-PR3: implementación real de IGenerarSolicitudCompraPort.
            // Cuando los stubs están off, OC real toma el cable; sin esta
            // registración el DI falla al resolver el handler de bifurcación.
            services.AddScoped<IGenerarSolicitudCompraPort, GenerarSolicitudCompraDesdeRqPort>();
            return services;
        }

        if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                "Compras:UseStubs=true no está permitido en Production. " +
                "Los stubs cross-module (InMemoryConsultarStockPort, " +
                "InMemoryGenerarSolicitudCompraPort) son provisionales y solo deben " +
                "activarse en entornos de desarrollo o test. Quitar el flag o " +
                "dejarlo en false para arrancar en producción.");
        }

        // El stub de stock se registra como concrete + alias de la interfaz.
        services.AddSingleton<InMemoryConsultarStockPort>();
        services.AddSingleton<IConsultarStockPort>(sp => sp.GetRequiredService<InMemoryConsultarStockPort>());

        // Este stub usa el DbContext (scoped), así que él mismo va Scoped.
        services.AddScoped<IGenerarSolicitudCompraPort, InMemoryGenerarSolicitudCompraPort>();

        return services;
    }
}
