using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Application.Integration.AlmCompra;

namespace Millet.Integraciones.Aw.Infrastructure.AlmCompra;

public static class AlmCompraDependencyInjection
{
    /// <summary>
    /// Transporte de la bandeja ERP → Almacenes (86e3jvu83): <c>Simulado</c> (por omisión) o <c>Sql</c>. El simulado es singleton
    /// y también se registra por su tipo para que las pruebas lo manejen.
    /// </summary>
    public static IServiceCollection AddAlmCompraTransporte(this IServiceCollection services, IConfiguration configuration)
    {
        var transporte = configuration[$"{AlmCompraOptions.SectionName}:{nameof(AlmCompraOptions.Transporte)}"];
        if (string.Equals(transporte, "Sql", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IAlmCompraTransporte, SqlAlmCompraTransporte>();
            return services;
        }
        if (!string.IsNullOrWhiteSpace(transporte) && !string.Equals(transporte, "Simulado", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Integraciones:AlmCompra:Transporte '{transporte}' no válido (Simulado | Sql).");
        services.AddSingleton<SimuladoAlmCompraTransporte>();
        services.AddSingleton<IAlmCompraTransporte>(sp => sp.GetRequiredService<SimuladoAlmCompraTransporte>());
        return services;
    }
}
