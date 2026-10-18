using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.ComercioExterior.Infrastructure.Workers;

/// <summary>
/// Avisos en la aplicación, sin correo; vencimientos en fecha de negocio (ADR-0040).
/// Mismo molde que <c>ReordenWorker</c>: intervalo + advisory lock PG. Espera un intervalo
/// antes del primer barrido; los avisos son únicos por hito y compromiso, así que repetir no duplica.
/// </summary>
public sealed class AvisosHitosWorker(IServiceScopeFactory scopes, IClock clock, IOptions<AvisosHitosWorkerOptions> options,
    ILogger<AvisosHitosWorker> logger) : BackgroundService
{
    private const string LockKey = "millet:comercio-exterior:avisos-hitos";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Disabled)
        {
            logger.LogInformation("AvisosHitosWorker deshabilitado (ComercioExterior:AvisosHitos:Disabled=true).");
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.Value.Interval, stoppingToken);
                await CorrerUnaVezAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "No se pudieron generar los avisos de hitos CE; se reintentará en la siguiente ejecución.");
            }
        }
    }

    public async Task<int> CorrerUnaVezAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        // Worker sin request: empresa explícita en cada agregado; no se expone este bypass por HTTP.
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        using var origin = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(AvisosHitosWorker));
        using var correlation = sp.GetRequiredService<IAuditCorrelationContext>().Begin(Guid.CreateVersion7());
        var db = sp.GetRequiredService<ComercioExteriorDbContext>();
        var hoy = clock.HoyLocal();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var adquirido = await db.Database.SqlQueryRaw<bool>(
                "SELECT pg_try_advisory_lock(hashtextextended({0}, 0)) AS \"Value\"", LockKey).SingleAsync(ct);
            if (!adquirido) return 0;
            try
            {
                var ids = await db.Expedientes.AsNoTracking()
                    .Where(e => e.Estado == EstadoExpediente.Abierto && e.Hitos.Any(h => h.FechaCompromiso < hoy && h.FechaReal == null && !h.Omitido))
                    .Select(e => new { e.Id, e.EmpresaId }).ToListAsync(ct);
                var cantidad = 0;
                foreach (var id in ids)
                {
                    await using var tx = await db.Database.BeginTransactionAsync(ct);
                    // Serializa el agregado con sus mutaciones HTTP y relee las fechas bajo el candado.
                    var e = await db.Expedientes.FromSqlInterpolated($"SELECT * FROM comercio_exterior.expedientes WHERE id = {id.Id} AND empresa_id = {id.EmpresaId} FOR UPDATE")
                        .Include(e => e.Hitos).Include(e => e.Avisos).AsSplitQuery().SingleAsync(ct);
                    cantidad += e.GenerarAvisos(hoy);
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    db.ChangeTracker.Clear();
                }
                logger.LogInformation("Avisos de hitos CE: {Cantidad} registrados para {FechaNegocio}.", cantidad, hoy);
                return cantidad;
            }
            finally
            {
                await db.Database.SqlQueryRaw<bool>(
                    "SELECT pg_advisory_unlock(hashtextextended({0}, 0)) AS \"Value\"", LockKey).SingleAsync(CancellationToken.None);
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}

public sealed class AvisosHitosWorkerOptions
{
    public const string SectionName = "ComercioExterior:AvisosHitos";

    public bool Disabled { get; set; }

    /// <summary>Cadencia del barrido. Default 1 hora.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
}
