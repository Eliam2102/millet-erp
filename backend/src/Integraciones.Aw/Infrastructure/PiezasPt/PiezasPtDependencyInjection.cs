using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Integraciones.Aw.Application.PiezasPt;
using Millet.Integraciones.Aw.Infrastructure.Adapters;
using Millet.Integraciones.Aw.Infrastructure.Origen;
using Millet.Integraciones.Aw.Infrastructure.OrigenPg;
using Millet.ProductoTerminado.Application.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Infrastructure.PiezasPt;

public static class PiezasPtDependencyInjection
{
    public static IServiceCollection AddAwPiezasPt(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<PiezasPtOptions>().Bind(config.GetSection(PiezasPtOptions.SectionName))
            .Validate(o => o.IntervalSeconds > 0 && o.BatchSize is > 0 and <= 1000 && o.MaxIntentos > 0 && o.PospuestoHoras > 0
                && o.TraslapeMinutos >= 0 && EstacionesValidas(o.EstacionesPt), "Revise intervalo, lote, reintentos, traslape y estaciones de PT.")
            .ValidateOnStart();
        var options = config.GetSection(PiezasPtOptions.SectionName).Get<PiezasPtOptions>() ?? new();

        services.AddKeyedScoped<IPiezasPtOrigenReader>("Demo", (_, _) => new PiezasPtOrigenAdapter(() => new AwOrigenPg.Fabrica(config).CreateConnection(), true));
        // Real solo con contrato I13.4 firmado, conexión configurada y bandera de entrada; no abre nada al registrarse.
        if (options.ContratoI134Firmado && !string.IsNullOrWhiteSpace(config.GetConnectionString("AwProduccionDb")) && options.EntradaHabilitada)
            services.AddKeyedScoped<IPiezasPtOrigenReader>("Real", (_, _) => new PiezasPtOrigenAdapter(
                () => new SqlConnection(AwSqlTls.Validar(config.GetConnectionString("AwProduccionDb")!, "AwProduccionDb")), false));

        services.AddScoped<IPiezasPtOrigenReader, PiezasPtOrigenSelector>();
        services.AddScoped<IPtSucursalPorClaveAwPort, PtSucursalPorClaveAwAdapter>();
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
