using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.ActivosFijos.Application;
using Millet.ActivosFijos.Infrastructure.PublicAdapters;
using Millet.Facturacion.Domain.Ports;
namespace Millet.ActivosFijos.Infrastructure;
public static class DependencyInjection
{
    public static IServiceCollection AddActivosFijosModule(this IServiceCollection services)
    {
        services.AddScoped<ActivoServicio>();
        services.AddScoped<LibroServicio>();
        services.AddScoped<CargaLibroServicio>();
        services.AddScoped<ReportesLibroServicio>();
        services.AddScoped<OperacionesLibroServicio>();
        services.AddScoped<VentaActivoConsumer>();
        services.AddScoped<IVentaActivoReadPort, Millet.Facturacion.Infrastructure.VentaActivoReadAdapter>();
        services.AddScoped<IConservacionReadPort, ConservacionReadAdapter>();
        services.AddScoped<Millet.SharedKernel.Application.Adjuntos.IAdjuntoPropietario, ActivoAdjuntoPropietario>();
        services.AddScoped<Millet.Compras.Domain.Ports.IClaseActivoCompraReadPort, ClaseActivoCompraReadAdapter>();
        services.AddScoped<ITransaccionPolizaActivo, TransaccionPolizaAdapter>();
        services.Replace(ServiceDescriptor.Scoped<Millet.Almacen.Domain.Ports.IActivoRecepcionReadPort, ActivoRecepcionReadAdapter>());
        services.AddScoped<CompraActivoConsumer>();
        services.AddScoped<ICatalogosActivoReadPort, CatalogosActivoReadAdapter>();
        services.Replace(ServiceDescriptor.Scoped<IActivosFijosReadPort, ActivosFijosReadAdapter>());
        return services;
    }
}
