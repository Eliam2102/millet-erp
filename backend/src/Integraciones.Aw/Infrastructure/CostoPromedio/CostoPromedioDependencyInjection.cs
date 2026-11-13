using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Integraciones.Aw.Infrastructure.CostoPromedio.Workers;

namespace Millet.Integraciones.Aw.Infrastructure.CostoPromedio;

public static class CostoPromedioDependencyInjection
{
    /// <summary>
    /// Publicación del costo promedio a Almacenes (M-447): transporte <c>Simulado</c> (por omisión, singleton y también por su tipo
    /// para las pruebas) o <c>Sql</c>, y el worker (apagado hasta <c>Habilitado=true</c>).
    /// </summary>
    public static IServiceCollection AddCostoPromedio(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CostoPromedioOptions>().Bind(configuration.GetSection(CostoPromedioOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<CostoPromedioLector>();
        services.AddScoped<CostoPromedioPublicador>();
        services.AddHostedService<CostoPromedioWorker>();
        var transporte = configuration[$"{CostoPromedioOptions.SectionName}:{nameof(CostoPromedioOptions.Transporte)}"];
        if (string.Equals(transporte, "Sql", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<ICostoPromedioTransporte, SqlCostoPromedioTransporte>();
            return services;
        }
        if (!string.IsNullOrWhiteSpace(transporte) && !string.Equals(transporte, "Simulado", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Integraciones:CostoPromedio:Transporte '{transporte}' no válido (Simulado | Sql).");
        services.AddSingleton<SimuladoCostoPromedioTransporte>();
        services.AddSingleton<ICostoPromedioTransporte>(sp => sp.GetRequiredService<SimuladoCostoPromedioTransporte>());
        return services;
    }
}
