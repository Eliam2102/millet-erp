using Microsoft.Extensions.DependencyInjection;

namespace Millet.ProductoTerminado.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddProductoTerminadoModule(this IServiceCollection services)
    {
        return services;
    }
}
