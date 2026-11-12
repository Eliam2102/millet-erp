using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.ComercioExterior.Application.Ports;
using Millet.Integraciones.Aw.Infrastructure.Adapters;
using Millet.Integraciones.Aw.Infrastructure.Origen;
using Millet.Integraciones.Aw.Infrastructure.OrigenPg;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Infrastructure.Exportacion;
public static class OtrDependencyInjection
{
    // Real lee OTR (otra base de la misma instancia que MILMAIN) con la conexión de sólo lectura de clientes; AwIntegracionDb no interviene.
    private const string Cadena = Millet.Integraciones.Aw.Application.Clientes.AwClientesOptions.ConnectionStringName;
    public static IServiceCollection AddAwOtrExportacion(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<OtrOptions>().Bind(config.GetSection(OtrOptions.SectionName))
            .Validate(o => o.IntervalSeconds > 0 && o.BatchSize is > 0 and <= 1000 && o.MaxIntentos > 0 && OtrOptions.ListaValida(o.EstatusRutaExcluidos) && OtrOptions.ListaValida(o.EstatusRutaCancelada) && !string.IsNullOrWhiteSpace(o.UnidadLinea), "Revise intervalo, lote, MaxIntentos, estatus de ruta y unidad de OTR.").ValidateOnStart();
        var options = config.GetSection(OtrOptions.SectionName).Get<OtrOptions>() ?? new();
        var historico = config.GetSection(Millet.Facturacion.Domain.Ports.HistoricoSapOptions.SectionName).Get<Millet.Facturacion.Domain.Ports.HistoricoSapOptions>() ?? new();
        void Registrar(string key, bool pg)
        {
            services.AddKeyedScoped<OtrTablasAdapter>(key, (sp, _) => new(() => pg
                ? new AwOrigenPg.Fabrica(config).CreateConnection()
                : new SqlConnection(AwSqlTls.Validar(config.GetConnectionString(Cadena)!, Cadena)), pg,
                pg ? null : ct => YaLeidoAsync(sp.GetRequiredService<ComercioExteriorDbContext>(), options.EmpresaId, ct), options, historico));
            if (key == "Demo" || options.EntradaHabilitada)
                services.AddKeyedScoped<IOtrEntradaReader>(key, (sp, _) => sp.GetRequiredKeyedService<OtrTablasAdapter>(key));
        }
        Registrar("Demo", true);
        if (options.ContratoI134Firmado && !string.IsNullOrWhiteSpace(config.GetConnectionString(Cadena)) && options.EntradaHabilitada) Registrar("Real", false);
        services.AddScoped<ISucursalOtrReadPort, Millet.Integraciones.Aw.Infrastructure.Pedidos.SucursalPorClaveAwResolver>();
        services.AddScoped<OtrOrigenSelector>();
        services.AddScoped<IOtrEntradaReader>(sp => sp.GetRequiredService<OtrOrigenSelector>());
        services.AddHostedService<OtrEntradaWorker>();
        return services;
    }
    /// <summary>Control del ERP: un envío ya está leído si su versión ya se aplicó o quedó rechazada; los errores técnicos se reintentan.</summary>
    public static async Task<Func<string, long, bool>> YaLeidoAsync(ComercioExteriorDbContext db, Guid? empresa, CancellationToken ct)
    {
        var aplicadas = await db.OtrEnvios.AsNoTracking().Where(x => x.EmpresaId == empresa).ToDictionaryAsync(x => x.IdOrigen, x => x.VersionAplicada, ct);
        var rechazadas = (await db.OtrEntradasControl.AsNoTracking().Where(x => x.EmpresaId == empresa && x.Rechazada).Select(x => new { x.IdOrigen, x.VersionOrigen }).ToListAsync(ct))
            .Select(x => (x.IdOrigen, x.VersionOrigen)).ToHashSet();
        return (id, version) => (aplicadas.TryGetValue(id, out var v) && version <= v) || rechazadas.Contains((id, version));
    }
}
public sealed class OtrOrigenSelector(IServiceProvider services, AwOrigenSesion sesion) : IOtrEntradaReader
{
    private async Task<T> ResolverAsync<T>(CancellationToken ct) where T : class =>
        services.GetKeyedService<T>(await sesion.EsDemoAsync(ct) ? "Demo" : "Real")
            ?? throw new BusinessRuleException("CE_OTR_ORIGEN_NO_HABILITADO", "Use Demo mientras no esté firmado I13.4; revise conexión y bandera de entrada.");
    public async Task<IReadOnlyList<OtrEntrada>> LeerPendientesAsync(int max, CancellationToken ct)
    { sesion.Reiniciar(); return await (await ResolverAsync<IOtrEntradaReader>(ct)).LeerPendientesAsync(max, ct); }
    public async Task MarcarResultadoAsync(string idOrigen, long version, string resultado, string? motivo, CancellationToken ct) =>
        await (await ResolverAsync<IOtrEntradaReader>(ct)).MarcarResultadoAsync(idOrigen, version, resultado, motivo, ct);
}
