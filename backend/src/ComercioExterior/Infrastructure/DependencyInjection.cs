using Microsoft.Extensions.DependencyInjection;
using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Adjuntos;

namespace Millet.ComercioExterior.Infrastructure;
public static class DependencyInjection
{
    public static IServiceCollection AddComercioExteriorModule(this IServiceCollection services)
    {
        services.AddScoped<ExpedienteServicio>();
        services.AddScoped<FinanzasServicio>();
        services.AddScoped<OrdenesServicioServicio>();
        services.AddScoped<Millet.ComercioExterior.Application.Ports.ITipoCambioGastoReadPort, TipoCambioGastoReadAdapter>();
        services.AddScoped<ContenedoresServicio>();
        services.AddScoped<OtrServicio>();
        services.AddScoped<Millet.ComercioExterior.Application.PublicPorts.IEmbarqueExportacionReadPort, EmbarqueExportacionReadAdapter>();
        services.AddScoped<ICatalogosCeReadPort, CatalogosCeReadAdapter>();
        services.AddScoped<IAdjuntoPropietario, ExpedienteAdjuntoPropietario>();
        services.AddScoped<IComercioExteriorSucursalReadPort, ComercioExteriorSucursalReadAdapter>();
        return services;
    }
}
