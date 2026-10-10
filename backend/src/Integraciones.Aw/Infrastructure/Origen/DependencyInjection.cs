using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Origen;
using Millet.Integraciones.Aw.Infrastructure.OrigenPg;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;

namespace Millet.Integraciones.Aw.Infrastructure.Origen;

public static class OrigenDependencyInjection
{
    /// <summary>Después del wiring vigente de todas las áreas: conserva íntegros sus registros como Real.</summary>
    public static IServiceCollection AddIntegracionesAwOrigen(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IAwOrigenActivo, AwOrigenActivo>();
        services.AddScoped<AwOrigenSesion>();
        services.AddScoped<AwOrigenSelectores>();
        ConservarReal<IAwClientesOrigen>(services);
        ConservarReal<IAwProductosOrigen>(services);
        ConservarReal<IAwSolicitudesReader>(services);
        ConservarReal<IAwWriteBackPort>(services);
        ConservarReal<IAwClientesReader>(services);
        ConservarReal<IAwArticulosReader>(services);
        ConservarReal<IMasterProvisioningPort>(services);

        services.AddOptions<AwPedidosOptions>().Bind(configuration.GetSection(AwPedidosOptions.SectionName));
        services.AddScoped<ISucursalPorClaveAwResolver, SucursalPorClaveAwResolver>();
        services.AddScoped<ICanalVentaPorClaveAwResolver, CanalVentaPorClaveAwResolver>();
        services.AddKeyedScoped<IIntegracionSqlConnectionFactory>("Demo", (_, _) => new AwOrigenPg.Fabrica(configuration));
        services.AddKeyedScoped<IAwClientesOrigen>("Demo", (sp, _) => new AwClientesPgOrigen(
            sp.GetRequiredKeyedService<IIntegracionSqlConnectionFactory>("Demo"), sp.GetRequiredService<IOptions<AwClientesOptions>>(),
            sp.GetRequiredService<ILogger<AwClientesPgOrigen>>()));
        services.AddKeyedScoped<IAwProductosOrigen>("Demo", (sp, _) => new AwProductosPgOrigen(
            sp.GetRequiredKeyedService<IIntegracionSqlConnectionFactory>("Demo"), sp.GetRequiredService<IOptions<AwProductosOptions>>(),
            sp.GetRequiredService<ILogger<AwProductosPgOrigen>>()));
        services.AddKeyedScoped<IAwSolicitudesReader>("Demo", (sp, _) => new AwSolicitudesPgReader(
            sp.GetRequiredKeyedService<IIntegracionSqlConnectionFactory>("Demo"), sp.GetRequiredService<ISucursalPorClaveAwResolver>(),
            sp.GetRequiredService<ICanalVentaPorClaveAwResolver>(), sp.GetRequiredService<IOptions<AwPedidosOptions>>(),
            sp.GetRequiredService<ILogger<AwSolicitudesPgReader>>()));
        services.AddKeyedScoped<IAwWriteBackPort>("Demo", (sp, _) => new AwWriteBackPgAdapter(
            sp.GetRequiredKeyedService<IIntegracionSqlConnectionFactory>("Demo"), sp.GetRequiredService<IOptions<AwPedidosOptions>>(),
            sp.GetRequiredService<ILogger<AwWriteBackPgAdapter>>()));
        services.AddKeyedScoped<IAwClientesReader>("Demo", (sp, _) => new AwClientesPgReader(
            sp.GetRequiredKeyedService<IIntegracionSqlConnectionFactory>("Demo"), sp.GetRequiredService<IOptions<AwPedidosOptions>>(),
            sp.GetRequiredService<ILogger<AwClientesPgReader>>()));
        services.AddKeyedScoped<IAwArticulosReader>("Demo", (sp, _) => new AwArticulosPgReader(
            sp.GetRequiredKeyedService<IIntegracionSqlConnectionFactory>("Demo"), sp.GetRequiredService<IOptions<AwPedidosOptions>>(),
            sp.GetRequiredService<ILogger<AwArticulosPgReader>>()));
        services.AddKeyedScoped<IMasterProvisioningPort>("Demo", (sp, _) => ActivatorUtilities.CreateInstance<AwMasterProvisioningAdapter>(sp));
        return services;
    }

    private static void ConservarReal<T>(IServiceCollection services) where T : class
    {
        var registros = services.Where(d => d.ServiceType == typeof(T) && !d.IsKeyedService).ToArray();
        var real = registros.LastOrDefault();
        foreach (var d in registros) services.Remove(d);
        if (real is not null)
            services.Add(new ServiceDescriptor(typeof(T), "Real", (sp, _) =>
                real.ImplementationInstance ?? real.ImplementationFactory?.Invoke(sp)
                ?? ActivatorUtilities.CreateInstance(sp, real.ImplementationType!), real.Lifetime));
        services.AddScoped<T>(sp => (T)(object)sp.GetRequiredService<AwOrigenSelectores>());
    }
}
