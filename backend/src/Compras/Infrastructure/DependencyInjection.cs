using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Application.Integration.AlmCompra;
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

    /// <summary>Bandeja ERP → Almacenes (86e3jvu83). El transporte (<see cref="IAlmCompraTransporte"/>) lo registra Integraciones.Aw.</summary>
    public static IServiceCollection AddComprasAlmCompra(this IServiceCollection services)
    {
        services.AddScoped<AlmCompraEncolador>();
        services.AddScoped<AlmCompraProcesador>();
        services.AddScoped<IAlmCompraCancelacion, AlmCompraCancelacion>();
        return services;
    }
}
