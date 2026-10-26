using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Application.PublicPorts;
using Millet.Compras.Infrastructure.PublicAdapters;

namespace Millet.Compras.Infrastructure;
public static class DependencyInjection
{
    public static IServiceCollection AddComprasReadPorts(this IServiceCollection services)
    {
        services.AddScoped<IOcImportacionReadPort, OcImportacionReadAdapter>();
        services.AddScoped<IOrdenesServicioReadPort, OrdenesServicioReadAdapter>();
        return services;
    }
}
