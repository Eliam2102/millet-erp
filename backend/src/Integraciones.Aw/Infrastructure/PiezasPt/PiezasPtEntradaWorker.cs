using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Infrastructure;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Infrastructure.PiezasPt;
public sealed class PiezasPtEntradaWorker(IServiceScopeFactory scopes, IOptions<PiezasPtOptions> options,
    ILogger<PiezasPtEntradaWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ct = stoppingToken;
        if (!Enabled) return;
        while (!ct.IsCancellationRequested)
            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.IntervalSeconds), ct); await RunOnceAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "El ciclo de piezas PT se reintentará; el cursor conserva el último escaneo persistido."); }
    }
    private DateTimeOffset ToOrigenOffset(DateTime local)
    {
        var zona = TimeZoneInfo.FindSystemTimeZoneById(options.Value.ZonaHorariaOrigen);
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, zona.GetUtcOffset(unspecified));
    }
    private bool Enabled => options.Value.EntradaHabilitada && options.Value.EmpresaId is Guid id && id != Guid.Empty;
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        if (!Enabled) return 0;
        using var scope = scopes.CreateScope(); var sp = scope.ServiceProvider;
        using var empresa = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        using var origin = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(PiezasPtEntradaWorker));
        using var correlation = sp.GetRequiredService<IAuditCorrelationContext>().Begin(Guid.CreateVersion7());
        var db = sp.GetRequiredService<ProductoTerminadoDbContext>();
        await db.Database.OpenConnectionAsync(ct); var acquired = false;
        var key = $"millet:pt:worker:{options.Value.EmpresaId}";
        try
        {
            acquired = await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_lock(hashtextextended({0}, 0)) AS \"Value\"", key).SingleAsync(ct);
            if (!acquired) return 0;
            var (channel, reader) = await sp.GetRequiredService<PiezasPtOrigenSelector>().SelectAsync(ct);
            return await ProcessAsync(sp, db, channel, reader, ct);
        }
        finally
        {
            if (acquired) await db.Database.SqlQueryRaw<bool>("SELECT pg_advisory_unlock(hashtextextended({0}, 0)) AS \"Value\"", key).SingleAsync(CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }
    public async Task<int> ProcessAsync(IServiceProvider sp, ProductoTerminadoDbContext db, string channel, IPiezasPtReader reader, CancellationToken ct)
    {
        var companyId = options.Value.EmpresaId!.Value;
        var clock = sp.GetRequiredService<IClock>(); var handler = sp.GetRequiredService<PtScanHandler>();
        var resolver = sp.GetRequiredService<ISucursalPorClaveAwResolver>();
        var watermark = await db.Watermarks.SingleOrDefaultAsync(w => w.EmpresaId == companyId && w.Origin == channel, ct);
        if (watermark is null) { watermark = new() { EmpresaId = companyId, Origin = channel }; db.Watermarks.Add(watermark); await db.SaveChangesAsync(ct); }
        var count = 0;
        var retryBefore = clock.UtcNow.AddSeconds(-options.Value.IntervalSeconds);
        var expiredBefore = clock.UtcNow.AddHours(-options.Value.PospuestoHoras);
        var pendingIds = await db.Scans.Where(s => s.EmpresaId == companyId && s.Origen == channel && s.Resultado == "Pospuesto"
            && s.LastAttemptAt <= retryBefore && (s.Intentos < options.Value.MaxIntentos || s.FirstAttemptAt <= expiredBefore))
            .OrderBy(s => s.LastAttemptAt).Take(options.Value.BatchSize).Select(s => s.Id).ToListAsync(ct);
        foreach (var id in pendingIds)
        {
            await using var tx = await handler.BeginAsync(companyId, ct);
            var scan = await db.Scans.SingleAsync(s => s.Id == id, ct); await db.Entry(scan).ReloadAsync(ct);
            if (scan.Resultado == "Pospuesto") { await handler.ApplyAsync(scan, ct); await db.SaveChangesAsync(ct); count++; }
            if (tx is not null) await tx.CommitAsync(ct);
        }
        foreach (var row in await reader.ReadAsync(watermark, options.Value.BatchSize, options.Value.EstacionesPt, ct))
        {
            var station = options.Value.EstacionesPt.SingleOrDefault(s => s.Estacion == row.Station);
            var branch = station is null ? null : await resolver.ResolverAsync(station.SucursalClaveAw, ct);
            // Hora del origen sin zona: se interpreta en ZonaHorariaOrigen; el cursor conserva el valor original.
            var input = new RegistrarEscaneoPieza(companyId, channel, row.SourceEventId, row.OrderNumber, row.Position,
                row.PieceNumber, station?.SucursalClaveAw, branch, ToOrigenOffset(row.Time), row.Barcode);
            await handler.Handle(input, ct);
            // Sólo avanza tras persistir el resultado; un fallo aquí se recupera por idempotencia.
            watermark.Timestamp = row.Time; watermark.Barcode = row.Barcode; watermark.Station = row.Station;
            await db.SaveChangesAsync(ct); count++;
            db.ChangeTracker.Clear(); db.Watermarks.Attach(watermark);
        }
        return count;
    }
}
