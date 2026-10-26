using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
namespace Millet.ActivosFijos.Infrastructure.Workers;
public sealed class AvisosActivosOptions
{
    public const string SectionName = "ActivosFijos:Avisos";
    public bool Disabled { get; set; }
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
    public int DiasAnticipacion { get; set; } = 30;
    public Dictionary<Guid, int> DiasPorEmpresa { get; set; } = [];
    public int Dias(Guid empresa) => DiasPorEmpresa.GetValueOrDefault(empresa, DiasAnticipacion);
}
public sealed class AvisosActivosWorker(IServiceScopeFactory scopes, IClock clock, IOptions<AvisosActivosOptions> options, ILogger<AvisosActivosWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Disabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(options.Value.Interval, stoppingToken); await CorrerUnaVezAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "No se pudieron generar avisos de activos fijos; se reintentará en el siguiente intervalo."); }
        }
    }
    public async Task<int> CorrerUnaVezAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        using var origen = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(AvisosActivosWorker));
        using var correlacion = sp.GetRequiredService<IAuditCorrelationContext>().Begin(Guid.CreateVersion7());
        return await GenerarAsync(sp.GetRequiredService<ActivosFijosDbContext>(), clock.HoyLocal(), options.Value, ct);
    }
    public static async Task<int> GenerarAsync(ActivosFijosDbContext db, DateOnly hoy, AvisosActivosOptions opciones, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_xact_lock(hashtextextended('millet:af:avisos', 0)) AS \"Value\"").SingleAsync(ct)) return 0;
        var activos = await db.Activos.Where(x => x.Estado != EstadoActivo.DadoDeBaja).ToListAsync(ct);
        var cantidad = 0;
        foreach (var a in activos)
        {
            // Comparte el candado de las mutaciones del activo: un reemplazo no puede emitir un aviso obsoleto.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({a.Id.ToString()}, 0))", ct);
            var documentos = await db.Documentos.Where(x => x.ActivoId == a.Id && !x.Reemplazado && x.VigenteHasta != null).ToListAsync(ct);
            var mantenimientos = await db.Mantenimientos.Where(x => x.ActivoId == a.Id && x.Proximo != null).ToListAsync(ct);
            var fuentes = documentos.Where(x => DocumentoActivo.RequiereVigencia(x.Tipo)).Select(x => (x.Id, Fecha: x.VigenteHasta!.Value, Mantenimiento: false))
                .Concat(mantenimientos.Select(x => (x.Id, Fecha: x.Proximo!.Value, Mantenimiento: true)));
            foreach (var f in fuentes)
            {
                var tipo = AvisoActivo.TipoPendiente(f.Fecha, hoy, opciones.Dias(a.EmpresaId));
                if (tipo == null || await db.Avisos.AnyAsync(x => x.FuenteId == f.Id && x.Tipo == tipo && x.EmpresaId == a.EmpresaId, ct)) continue;
                db.Avisos.Add(new AvisoActivo(a, f.Id, f.Mantenimiento, tipo.Value, f.Fecha, hoy)); cantidad++;
            }
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return cantidad;
    }
}
