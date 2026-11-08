using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Ports;
using Millet.DatosMaestros.Application.Clientes;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Origen;
using Millet.Integraciones.Aw.Application.Retorno;
using Millet.Integraciones.Aw.Infrastructure.Adapters;
using Millet.Integraciones.Aw.Infrastructure.NotasCredito;
using Millet.Integraciones.Aw.Infrastructure.Retorno;
using Millet.Integraciones.Aw.Infrastructure.Clientes;
using Millet.Integraciones.Aw.Infrastructure.OrigenPg;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.Pinturas;
using Millet.Integraciones.Aw.Application.Pinturas;

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
        ConservarReal<IAwClientesPedidosOrigen>(services);
        ConservarReal<IAwProductosOrigen>(services);
        ConservarReal<IAwSolicitudesReader>(services);
        ConservarReal<IAwWriteBackPort>(services);
        // 86e3jvv6z / 86e3wjt5r: con la bandera, las NC se leen directo de A+W (solo SELECT, millet_erp_lector_aw) y no hay acuse hacia A+W.
        // Se registra después de la tabla puente (si existe) para ser el «Real»; Demo conserva su adaptador.
        // Solo con connection string real (una ref KV sin resolver se trata como ausente en arranque dev local).
        var lectura = configuration.GetSection(AwNotasCreditoLecturaOptions.SectionName).Get<AwNotasCreditoLecturaOptions>() ?? new();
        if (lectura.Habilitada)
        {
            const string cadena = AwClientesOptions.ConnectionStringName;
            var cs = configuration.GetConnectionString(cadena);
            if (!string.IsNullOrWhiteSpace(cs)
                && !cs.StartsWith("@Microsoft.KeyVault", StringComparison.OrdinalIgnoreCase))
            {
                services.AddSingleton<AwNotasCreditoVistas>();
                services.AddScoped<IAwNotasCreditoPort>(sp => new AwNotasCreditoDirectoPort(
                    new AwNotasCreditoLectorAdapter(() => new SqlConnection(AwSqlTls.Validar(cs, cadena)), lectura),
                    lectura, sp.GetRequiredService<AwNotasCreditoVistas>()));
            }
        }
        ConservarReal<IAwNotasCreditoPort>(services);
        ConservarReal<IAwRetornoPort>(services);
        services.AddScoped<AwRetornoDocumentoService>();
        ConservarReal<IAwClientesReader>(services);
        ConservarReal<IAwArticulosReader>(services);
        ConservarReal<IMasterProvisioningPort>(services);
        // I13.9: con AwIntegracionDb el Real es PinturasPedidosAdapter; sin ella, el stub de Facturación (0 pedidos).
        ConservarReal<IPlantaPinturaPedidosReader>(services);

        services.AddOptions<AwPedidosOptions>().Bind(configuration.GetSection(AwPedidosOptions.SectionName));
        services.AddScoped<ISucursalPorClaveAwResolver, SucursalPorClaveAwResolver>();
        services.AddScoped<ICanalVentaPorClaveAwResolver, CanalVentaPorClaveAwResolver>();
        services.AddKeyedScoped<IIntegracionSqlConnectionFactory>("Demo", (_, _) => new AwOrigenPg.Fabrica(configuration));
        services.AddKeyedScoped<IAwClientesOrigen>("Demo", (sp, _) => new AwClientesPgOrigen(
            sp.GetRequiredKeyedService<IIntegracionSqlConnectionFactory>("Demo"), sp.GetRequiredService<IOptions<AwClientesOptions>>(),
            sp.GetRequiredService<ILogger<AwClientesPgOrigen>>()));
        services.AddKeyedScoped<IAwClientesPedidosOrigen>("Demo", (_, _) => new AwClientesPedidosVacio());
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
        services.AddKeyedScoped<IAwNotasCreditoPort>("Demo", (sp, _) => new AwNotasCreditoAdapter(
            sp.GetRequiredKeyedService<IIntegracionSqlConnectionFactory>("Demo"), sp.GetRequiredService<IOptions<AwPedidosOptions>>()));
        services.AddKeyedScoped<IAwRetornoPort>("Demo",(sp, _) => new AwRetornoAdapter(
            sp.GetRequiredKeyedService<IIntegracionSqlConnectionFactory>("Demo"), sp.GetRequiredService<IOptions<AwPedidosOptions>>(),
            AwRetornoAdapter.CandadoPostgres));
        services.AddKeyedScoped<IAwClientesReader>("Demo", (sp, _) => new AwClientesPgReader(
            sp.GetRequiredKeyedService<IIntegracionSqlConnectionFactory>("Demo"), sp.GetRequiredService<IOptions<AwPedidosOptions>>(),
            sp.GetRequiredService<ILogger<AwClientesPgReader>>()));
        services.AddKeyedScoped<IAwArticulosReader>("Demo", (sp, _) => new AwArticulosPgReader(
            sp.GetRequiredKeyedService<IIntegracionSqlConnectionFactory>("Demo"), sp.GetRequiredService<IOptions<AwPedidosOptions>>(),
            sp.GetRequiredService<ILogger<AwArticulosPgReader>>()));
        services.AddKeyedScoped<IMasterProvisioningPort>("Demo", (sp, _) => ActivatorUtilities.CreateInstance<AwMasterProvisioningAdapter>(sp));
        PedidosDependencyInjection.AddPinturasPedidos(services, configuration);
        services.AddKeyedScoped<IPlantaPinturaPedidosReader>("Demo", (sp, _) => new PinturasPedidosAdapter(
            sp.GetRequiredKeyedService<IIntegracionSqlConnectionFactory>("Demo"), postgres: true,
            sp.GetRequiredService<IPinturasSucursalResolver>(), sp.GetRequiredService<IOptions<PinturasPedidosOptions>>(),
            sp.GetRequiredService<ILogger<PinturasPedidosAdapter>>()));
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
