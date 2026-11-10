using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Facturacion.Domain.Ports;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Pinturas;
using Millet.Integraciones.Aw.Application.Pinturas.Catalogos;
using Millet.Integraciones.Aw.Application.Retorno;
using Millet.Integraciones.Aw.Application.Workers;
using Microsoft.Extensions.Logging;
using Millet.Integraciones.Aw.Application.TipoCambio;
using Millet.Integraciones.Aw.Infrastructure.NotasCredito;
using Millet.Integraciones.Aw.Infrastructure.Pinturas;
using Millet.Integraciones.Aw.Infrastructure.Retorno;
using Millet.Integraciones.Aw.Infrastructure.TipoCambio;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Infrastructure.Pedidos;

/// <summary>
/// Wiring del flujo 2 — adapters reales de la ingesta de pedidos A+W →
/// Facturación (ADR-0048). Se invoca desde <c>Program.cs</c> DESPUÉS de
/// <c>AddFacturacionModule</c> (last-registration-wins pisa los stubs de F3)
/// y SOLO si hay <c>ConnectionStrings:AwIntegracionDb</c> — tercer candado
/// de ambientes (sin connection string → quedan los stubs y el flujo sigue
/// inerte, ver doc integration/04 y runbook de operación).
///
/// <para>Independiente del wiring del flujo 1 (<c>AddIntegracionesAwModule</c>,
/// cotizaciones del Glass Agent) — no comparten registros.</para>
/// </summary>
public static class PedidosDependencyInjection
{
    public static IServiceCollection AddIntegracionesAwPedidosAdapters(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AwPedidosOptions>()
            .Bind(configuration.GetSection(AwPedidosOptions.SectionName));

        services.AddSingleton<IIntegracionSqlConnectionFactory, IntegracionSqlConnectionFactory>();

        // Pisan StubAwSolicitudesReader / StubAwWriteBackPort (F3-PR1b).
        // Cierra <AwVistaPedidos> y la mitad A+W de <WriteBackOrigenes>.
        services.AddScoped<ISucursalPorClaveAwResolver, SucursalPorClaveAwResolver>();
        // FAC-ING-PR2: el canal también se resuelve por catálogo
        // (compartido.canales_venta.clave_aw = GRUPPE crudo de la vista).
        services.AddScoped<ICanalVentaPorClaveAwResolver, CanalVentaPorClaveAwResolver>();
        services.AddScoped<IAwSolicitudesReader, AwSolicitudesSqlReader>();
        services.AddScoped<IAwWriteBackPort, AwWriteBackSqlAdapter>();
        // 86e3wjt5r: notas de crédito de A+W (tabla puente aw_solicitud_nota_credito).
        services.AddScoped<IAwNotasCreditoPort, AwNotasCreditoAdapter>();
        // TR-01: filas Documento de aw_retorno (único escritor hacia A+W por factura).
        services.AddScoped<IAwRetornoPort>(sp => new AwRetornoAdapter(
            sp.GetRequiredService<IIntegracionSqlConnectionFactory>(), sp.GetRequiredService<IOptions<AwPedidosOptions>>(),
            AwRetornoAdapter.CandadoSqlServer));
        // I13.2: publicación de tipo de cambio en dbo.erp_tipo_cambio.
        services.AddScoped<IAwTipoCambioPort>(sp => new AwTipoCambioAdapter(
            sp.GetRequiredService<IIntegracionSqlConnectionFactory>(), sp.GetRequiredService<IOptions<AwPedidosOptions>>(),
            AwTipoCambioAdapter.CandadoSqlServer));

        // PR4 — auto-provisión de masters (cierra <MasterProvisioningAw>):
        // readers de vw_erp_cliente/vw_erp_articulo + bridge que delega el
        // alta a los commands de DatosMaestros (pisa NoOpMasterProvisioningPort).
        services.AddScoped<IAwClientesReader, AwClientesSqlReader>();
        services.AddScoped<IAwArticulosReader, AwArticulosSqlReader>();
        services.AddScoped<IMasterProvisioningPort, AwMasterProvisioningAdapter>();

        // I13.9: pedidos de Planta Pintura en dbo.pinturas_pedido de la misma base (script 16 v2). Pisa
        // StubPlantaPinturaPedidosReader (cierra <PlantaPinturaOrigenes> en la lectura y el acuse).
        AddPinturasPedidos(services, configuration);
        services.AddScoped<IPlantaPinturaPedidosReader>(sp => new PinturasPedidosAdapter(
            sp.GetRequiredService<IIntegracionSqlConnectionFactory>(), postgres: false,
            sp.GetRequiredService<IPinturasSucursalResolver>(), sp.GetRequiredService<IOptions<PinturasPedidosOptions>>(),
            sp.GetRequiredService<ILogger<PinturasPedidosAdapter>>()));

        // Publicador de catálogos hacia Planta Pintura (script 16 v2).
        services.AddScoped<IPinturasCatalogosPublisher>(sp => new PinturasCatalogosAdapter(
            sp.GetRequiredService<IIntegracionSqlConnectionFactory>(),
            postgres: false,
            sp.GetRequiredService<IOptions<PinturasCatalogosOptions>>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ILogger<PinturasCatalogosAdapter>>()));

        services.AddHostedService<PinturasCatalogosSyncWorker>();

        return services;
    }

    /// <summary>Opciones y resolución de sucursal del lector de Pinturas (las comparten el origen Real y el Demo).</summary>
    internal static void AddPinturasPedidos(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PinturasPedidosOptions>().Bind(configuration.GetSection(PinturasPedidosOptions.SectionName));
        services.AddScoped<IPinturasSucursalResolver, PinturasSucursalResolver>();

        services.AddOptions<PinturasCatalogosOptions>().Bind(configuration.GetSection(PinturasCatalogosOptions.SectionName));
        services.AddScoped<IPinturasObrasSource, NoOpPinturasObrasSource>();
        services.AddScoped<IPinturasCatalogosExtractor, PinturasCatalogosExtractor>();
    }
}
