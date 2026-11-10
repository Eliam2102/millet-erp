using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.PiezasPt;
using Millet.Integraciones.Aw.Infrastructure.ConsumoComponentes;
using Millet.Integraciones.Aw.Infrastructure.Adapters;
using Millet.Integraciones.Aw.Infrastructure.Origen;
using Millet.Integraciones.Aw.Infrastructure.OrigenPg;
using Millet.Integraciones.Aw.Infrastructure.Taller;
using Millet.Integraciones.Aw.Infrastructure.Taller.Workers;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Infrastructure.PiezasPt;

public static class PiezasPtDependencyInjection
{
    public static IServiceCollection AddAwPiezasPt(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<PiezasPtOptions>().Bind(config.GetSection(PiezasPtOptions.SectionName))
            .Validate(o => o.IntervalSeconds > 0 && o.BatchSize is > 0 and <= 1000 && o.MaxIntentos > 0 && o.PospuestoHoras > 0
                && o.TraslapeMinutos >= 0 && o.BarridoMinutos > 0 && o.AlbaranAwVentanaDias > 0 && EstacionesValidas(o.EstacionesPt), "Revise intervalo, lote, reintentos, traslape, barrido y estaciones de PT.")
            .ValidateOnStart();
        var options = config.GetSection(PiezasPtOptions.SectionName).Get<PiezasPtOptions>() ?? new();

        services.AddKeyedScoped<IPiezasPtOrigenReader>("Demo", (_, _) => new PiezasPtOrigenAdapter(() => new AwOrigenPg.Fabrica(config).CreateConnection(), true));
        // Real solo con contrato I13.4 firmado, conexión configurada y bandera de entrada; no abre nada al registrarse.
        // Misma conexión de clientes (millet_erp_lector_aw); alcimdb se alcanza por el servidor vinculado AWPROD_ERP (SRC-114), sin cadena propia.
        const string cadena = AwClientesOptions.ConnectionStringName;
        if (options.ContratoI134Firmado && !string.IsNullOrWhiteSpace(config.GetConnectionString(cadena)) && options.EntradaHabilitada)
            services.AddKeyedScoped<IPiezasPtOrigenReader>("Real", (_, _) => new PiezasPtOrigenAdapter(
                () => new SqlConnection(AwSqlTls.Validar(config.GetConnectionString(cadena)!, cadena)), false));

        services.AddScoped<IPiezasPtOrigenReader, PiezasPtOrigenSelector>();

        // A4.8: mismo origen (Demo/Real), misma conexión y mismo servidor vinculado que PT; solo SELECT.
        services.AddKeyedScoped<IConsumoComponentesOrigenReader>("Demo", (_, _) => new ConsumoComponentesOrigenAdapter(() => new AwOrigenPg.Fabrica(config).CreateConnection(), true));
        if (options.ContratoI134Firmado && !string.IsNullOrWhiteSpace(config.GetConnectionString(cadena)) && options.ConsumoComponentesHabilitado)
            services.AddKeyedScoped<IConsumoComponentesOrigenReader>("Real", (_, _) => new ConsumoComponentesOrigenAdapter(
                () => new SqlConnection(AwSqlTls.Validar(config.GetConnectionString(cadena)!, cadena)), false));
        services.AddScoped<IConsumoComponentesOrigenReader, ConsumoComponentesOrigenSelector>();
        services.AddHostedService<ConsumoComponentesWorker>();

        // PT5.10: pedidos internos Serie 9000 → descarga del inventario del taller. Real lee las vistas de pedido de la BD de integración.
        services.AddKeyedScoped<IPedidosInternosOrigenReader>("Demo", (_, _) => new PedidosInternosOrigenAdapter(() => new AwOrigenPg.Fabrica(config).CreateConnection(), true));
        const string cadenaIntegracion = "AwIntegracionDb";
        if (options.ContratoI134Firmado && !string.IsNullOrWhiteSpace(config.GetConnectionString(cadenaIntegracion)) && options.Serie9000Habilitada)
            services.AddKeyedScoped<IPedidosInternosOrigenReader>("Real", (_, _) => new PedidosInternosOrigenAdapter(
                () => new SqlConnection(AwSqlTls.Validar(config.GetConnectionString(cadenaIntegracion)!, cadenaIntegracion)), false));
        services.AddScoped<IPedidosInternosOrigenReader, PedidosInternosOrigenSelector>();
        services.AddScoped<Millet.Almacen.Application.Taller.IPedidosTallerDiaReadPort, PedidosTallerDiaReadAdapter>();
        services.AddHostedService<Serie9000Worker>();

        // Albaranes de A+W → descuento de PT (script 20, vw_erp_albaran_pos). Real solo con I13.4, conexión de integración y bandera.
        services.AddKeyedScoped<IAlbaranesAwOrigenReader>("Demo", (sp, _) => new AlbaranesAwOrigenAdapter(() => new AwOrigenPg.Fabrica(config).CreateConnection(), true,
            sp.GetRequiredService<ILogger<AlbaranesAwOrigenAdapter>>()));
        if (options.ContratoI134Firmado && !string.IsNullOrWhiteSpace(config.GetConnectionString(cadenaIntegracion)) && options.AlbaranAwHabilitado)
            services.AddKeyedScoped<IAlbaranesAwOrigenReader>("Real", (sp, _) => new AlbaranesAwOrigenAdapter(
                () => new SqlConnection(AwSqlTls.Validar(config.GetConnectionString(cadenaIntegracion)!, cadenaIntegracion)), false,
                sp.GetRequiredService<ILogger<AlbaranesAwOrigenAdapter>>()));
        services.AddScoped<IAlbaranesAwOrigenReader, AlbaranesAwOrigenSelector>();
        services.AddHostedService<Workers.AlbaranesAwWorker>();
        services.AddScoped<IPtSucursalPorClaveAwPort, PtSucursalPorClaveAwAdapter>();
        services.AddScoped<ISaldoPedidoReadPort, SaldoPedidoSelector>();
        services.AddHostedService<PiezasPtEntradaWorker>();
        services.AddHostedService<PiezasPtLiberacionWorker>();
        return services;
    }

    private static bool EstacionesValidas(string[] estaciones)
    {
        try { EstacionesPtFiltro.Parse(estaciones ?? []); return true; } catch (ArgumentException) { return false; }
    }
}

public sealed class PiezasPtOrigenSelector(IServiceProvider services, AwOrigenSesion sesion) : IPiezasPtOrigenReader
{
    private async Task<IPiezasPtOrigenReader> ResolverAsync(CancellationToken ct) =>
        services.GetKeyedService<IPiezasPtOrigenReader>(await sesion.EsDemoAsync(ct) ? "Demo" : "Real")
            ?? throw new BusinessRuleException("PT_ORIGEN_NO_HABILITADO", "Use Demo mientras no esté firmado I13.4; revise la conexión y la bandera de entrada de PT.");

    public async Task<string> OrigenActualAsync(CancellationToken ct) => await sesion.EsDemoAsync(ct) ? "Demo" : "Real";

    public async Task<IReadOnlyList<EscaneoOrigen>> LeerAsync(DateTime desde, (DateTime Tiempo, string Etiqueta)? despuesDe,
        IReadOnlyList<(int Desde, int Hasta)> estaciones, int max, CancellationToken ct) =>
        await (await ResolverAsync(ct)).LeerAsync(desde, despuesDe, estaciones, max, ct);
}

/// <summary>
/// Saldo por pedido según el origen de A+W activo: Demo usa el saldo de prueba; Real, el saldo de Facturación y la cartera de CxC (U-3).
/// </summary>
public sealed class SaldoPedidoSelector(IServiceProvider services, AwOrigenSesion sesion) : ISaldoPedidoReadPort
{
    public async Task<decimal?> ObtenerAsync(Guid empresaId, string numeroPedido, CancellationToken ct) =>
        await (await sesion.EsDemoAsync(ct)
            ? (ISaldoPedidoReadPort)services.GetRequiredService<SaldoPedidoDemoAdapter>()
            : services.GetRequiredService<Millet.Facturacion.Infrastructure.PublicAdapters.SaldoPedidoReadAdapter>()).ObtenerAsync(empresaId, numeroPedido, ct);
}
