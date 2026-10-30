using Microsoft.Extensions.DependencyInjection;
using Millet.ProductoTerminado.Application.Escaneos;
using Millet.ProductoTerminado.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;

namespace Millet.ProductoTerminado.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddProductoTerminadoModule(this IServiceCollection services)
    {
        services.AddScoped<EscaneoProcesador>();
        services.AddScoped<Application.PtAcceso>();
        services.AddScoped<IProductoTerminadoSucursalReadPort, ProductoTerminadoSucursalReadAdapter>();
        return services;
    }
}
