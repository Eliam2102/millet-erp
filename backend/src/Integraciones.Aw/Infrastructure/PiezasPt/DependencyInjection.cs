using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Millet.Integraciones.Aw.Infrastructure.Adapters;
using Millet.Integraciones.Aw.Infrastructure.Origen;
using Millet.Integraciones.Aw.Infrastructure.OrigenPg;
using Millet.ProductoTerminado.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Infrastructure.PiezasPt;
public static class PiezasPtDependencyInjection
{
    public static IServiceCollection AddAwPiezasPt(this IServiceCollection services, IConfiguration config, IHostEnvironment environment)
    {
        services.AddOptions<PiezasPtOptions>().Bind(config.GetSection(PiezasPtOptions.SectionName))
            .Validate(o => o.IntervalSeconds > 0 && o.BatchSize is > 0 and <= 1000 && o.MaxIntentos > 0 && o.PospuestoHoras > 0
                && o.EstacionesPt.All(s => s.Estacion > 0 && !string.IsNullOrWhiteSpace(s.SucursalClaveAw))
                && o.EstacionesPt.Select(s => s.Estacion).Distinct().Count() == o.EstacionesPt.Count,
                "Revise intervalo, lote, intentos y estaciones de producto terminado.").ValidateOnStart();
        services.AddOptions<PtScanOptions>().Bind(config.GetSection(PiezasPtOptions.SectionName));
        var options = config.GetSection(PiezasPtOptions.SectionName).Get<PiezasPtOptions>() ?? new();
        if (!environment.IsProduction())
            services.AddKeyedScoped<IPiezasPtReader>("Demo", (_, _) => new PiezasPtReader(() => new AwOrigenPg.Fabrica(config).CreateConnection(), true));
        // PLATFORM-TODO(<PiezasPtCanalReal>): A+W-QA-7 / I13.4 y tercera Hybrid Connection hacia AWPRODUCTION.
        if (options.EntradaHabilitada && options.ContratoI134Firmado && !string.IsNullOrWhiteSpace(config.GetConnectionString("AwProduccionDb")))
            services.AddKeyedScoped<IPiezasPtReader>("Real", (_, _) => new PiezasPtReader(() =>
                new SqlConnection(AwSqlTls.Validar(config.GetConnectionString("AwProduccionDb")!, "AwProduccionDb")), false));
        services.AddScoped<PiezasPtOrigenSelector>();
        services.AddHostedService<PiezasPtEntradaWorker>();
        return services;
    }
}
public sealed class PiezasPtOrigenSelector(IServiceProvider services, AwOrigenSesion session)
{
    public async Task<(string Origin, IPiezasPtReader Reader)> SelectAsync(CancellationToken ct)
    {
        session.Reiniciar(); var origin = await session.EsDemoAsync(ct) ? "Demo" : "Real";
        return (origin, services.GetKeyedService<IPiezasPtReader>(origin)
            ?? throw new BusinessRuleException("PT_ORIGEN_NO_HABILITADO", "Revise el contrato y la configuración del lector de producto terminado."));
    }
}
