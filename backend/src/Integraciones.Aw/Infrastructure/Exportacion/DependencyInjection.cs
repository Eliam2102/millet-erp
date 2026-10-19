using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.ComercioExterior.Application.Ports;
using Millet.Integraciones.Aw.Infrastructure.Origen;
using Millet.Integraciones.Aw.Infrastructure.OrigenPg;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Infrastructure.Exportacion;
public static class OtrDependencyInjection
{
    public static IServiceCollection AddAwOtrExportacion(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<OtrOptions>().Bind(config.GetSection(OtrOptions.SectionName))
            .Validate(o => o.IntervalSeconds > 0 && o.BatchSize is > 0 and <= 1000 && o.MaxIntentos > 0 && o.AcuseVencidoMinutos > 0, "Revise intervalo, lote y MaxIntentos de OTR.").ValidateOnStart();
        var options = config.GetSection(OtrOptions.SectionName).Get<OtrOptions>() ?? new();
        void Registrar(string key, bool pg)
        {
            services.AddKeyedScoped<OtrTablasAdapter>(key, (_, _) => new(() => pg
                ? new AwOrigenPg.Fabrica(config).CreateConnection()
                : new SqlConnection(config.GetConnectionString("AwIntegracionDb")), pg));
            if (key == "Demo" || options.EntradaHabilitada)
                services.AddKeyedScoped<IOtrEntradaReader>(key, (sp, _) => sp.GetRequiredKeyedService<OtrTablasAdapter>(key));
            if (key == "Demo" || options.SalidaHabilitada)
                services.AddKeyedScoped<IOtrSalidaWriter>(key, (sp, _) => sp.GetRequiredKeyedService<OtrTablasAdapter>(key));
        }
        Registrar("Demo", true);
        if (options.ContratoI134Firmado && !string.IsNullOrWhiteSpace(config.GetConnectionString("AwIntegracionDb")) && (options.EntradaHabilitada || options.SalidaHabilitada)) Registrar("Real", false);
        services.AddScoped<OtrOrigenSelector>();
        services.AddScoped<IOtrEntradaReader>(sp => sp.GetRequiredService<OtrOrigenSelector>());
        services.AddScoped<IOtrSalidaWriter>(sp => sp.GetRequiredService<OtrOrigenSelector>());
        services.AddHostedService<OtrEntradaWorker>(); services.AddHostedService<OtrSalidaWorker>();
        return services;
    }
}
public sealed class OtrOrigenSelector(IServiceProvider services, AwOrigenSesion sesion) : IOtrEntradaReader, IOtrSalidaWriter
{
    private async Task<T> ResolverAsync<T>(CancellationToken ct) where T : class =>
        services.GetKeyedService<T>(await sesion.EsDemoAsync(ct) ? "Demo" : "Real")
            ?? throw new BusinessRuleException("CE_OTR_ORIGEN_NO_HABILITADO", "Use Demo mientras no esté firmado I13.4; revise conexión y banderas independientes.");
    public async Task<IReadOnlyList<OtrEntrada>> LeerPendientesAsync(int max, CancellationToken ct)
    { sesion.Reiniciar(); return await (await ResolverAsync<IOtrEntradaReader>(ct)).LeerPendientesAsync(max, ct); }
    public async Task MarcarResultadoAsync(string idOrigen, long version, string resultado, string? motivo, CancellationToken ct) =>
        await (await ResolverAsync<IOtrEntradaReader>(ct)).MarcarResultadoAsync(idOrigen, version, resultado, motivo, ct);
    public async Task EntregarAsync(OtrEntrega salida, CancellationToken ct) => await (await ResolverAsync<IOtrSalidaWriter>(ct)).EntregarAsync(salida, ct);
    public async Task<IReadOnlyList<OtrAcuse>> LeerAcusesAsync(IReadOnlyCollection<Guid> salidas, CancellationToken ct) =>
        await (await ResolverAsync<IOtrSalidaWriter>(ct)).LeerAcusesAsync(salidas, ct);
}
