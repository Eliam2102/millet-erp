using Microsoft.Extensions.DependencyInjection;
using Millet.ProductoTerminado.Application.Escaneos;

namespace Millet.ProductoTerminado.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddProductoTerminadoModule(this IServiceCollection services)
    {
        services.AddScoped<EscaneoProcesador>();
        services.AddScoped<Application.PtAcceso>();
        return services;
    }
}
