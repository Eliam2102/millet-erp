using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.OrigenPg;

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

        if (OrigenPostgres(configuration))
            services.AddSingleton<IIntegracionSqlConnectionFactory>(
                new AwOrigenPg.Fabrica(configuration.GetConnectionString(AwOrigenPg.ConnectionStringName)!));
        else
            services.AddSingleton<IIntegracionSqlConnectionFactory, IntegracionSqlConnectionFactory>();

        // Pisan StubAwSolicitudesReader / StubAwWriteBackPort (F3-PR1b).
        // Cierra <AwVistaPedidos> y la mitad A+W de <WriteBackOrigenes>.
        services.AddScoped<ISucursalPorClaveAwResolver, SucursalPorClaveAwResolver>();
        // FAC-ING-PR2: el canal también se resuelve por catálogo
        // (compartido.canales_venta.clave_aw = GRUPPE crudo de la vista).
        services.AddScoped<ICanalVentaPorClaveAwResolver, CanalVentaPorClaveAwResolver>();
        services.AddScoped<IAwSolicitudesReader, AwSolicitudesSqlReader>();
        services.AddScoped<IAwWriteBackPort, AwWriteBackSqlAdapter>();

        // PR4 — auto-provisión de masters (cierra <MasterProvisioningAw>):
        // readers de vw_erp_cliente/vw_erp_articulo + bridge que delega el
        // alta a los commands de DatosMaestros (pisa NoOpMasterProvisioningPort).
        services.AddScoped<IAwClientesReader, AwClientesSqlReader>();
        services.AddScoped<IAwArticulosReader, AwArticulosSqlReader>();
        services.AddScoped<IMasterProvisioningPort, AwMasterProvisioningAdapter>();

        return services;
    }

    /// <summary>
    /// Origen de DEMO: la cola y las vistas se leen de las tablas <c>dbo.*</c> de la BD PostgreSQL de
    /// <c>tools/aw-origen-demo</c> en vez de MILLET_INTEGRACION. Exige <c>IntegracionesAw:Pedidos:Origen=Postgres</c>
    /// y <c>ConnectionStrings:AwOrigenPgDb</c>.
    /// </summary>
    public static bool OrigenPostgres(IConfiguration configuration) =>
        string.Equals(configuration[$"{AwPedidosOptions.SectionName}:Origen"], "Postgres", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(configuration.GetConnectionString(AwOrigenPg.ConnectionStringName));
}
