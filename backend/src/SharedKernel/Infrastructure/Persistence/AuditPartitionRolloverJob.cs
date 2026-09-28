using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application;

namespace Millet.SharedKernel.Infrastructure.Persistence;

/// <summary>
/// Prepara las particiones mensuales de core.audit_log antes de que lleguen
/// eventos. El arranque espera a que existan la partición actual y las tres
/// siguientes; después se revisan diariamente. La cuenta de la aplicación
/// requiere permiso CREATE sobre el esquema core.
/// </summary>
public sealed class AuditPartitionRolloverJob(
    IServiceScopeFactory scopeFactory,
    ILogger<AuditPartitionRolloverJob> logger) : IHostedService, IDisposable
{
    private const long AdvisoryLockId = 7_390_003L;
    private CancellationTokenSource? _stop;
    private Task? _loop;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await EnsurePartitionsAsync(cancellationToken);
        _stop = new CancellationTokenSource();
        _loop = RunAsync(_stop.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stop is null || _loop is null) return;
        await _stop.CancelAsync();
        try { await _loop.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public void Dispose() => _stop?.Dispose();

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try { await EnsurePartitionsAsync(ct); }
                catch (Exception ex)
                {
                    logger.LogError(ex, "No se pudieron preparar las particiones futuras de auditoría.");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    public async Task EnsurePartitionsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var first = new DateTime(clock.UtcNow.UtcDateTime.Year, clock.UtcNow.UtcDateTime.Month, 1);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync(
            $"SELECT pg_advisory_xact_lock({AdvisoryLockId})", ct);

        for (var offset = 0; offset <= 3; offset++)
        {
            var from = first.AddMonths(offset);
            var to = from.AddMonths(1);
            // Identificadores construidos exclusivamente a partir de enteros
            // calculados por el reloj, nunca a partir de un request.
            var name = $"audit_log_y{from:yyyy}m{from:MM}";
#pragma warning disable EF1002 // DDL: el identificador no acepta parámetros y sólo usa año/mes del reloj.
            await db.Database.ExecuteSqlRawAsync($"""
                CREATE TABLE IF NOT EXISTS core.{name} PARTITION OF core.audit_log
                FOR VALUES FROM ('{from:yyyy-MM-dd} 00:00:00+00')
                TO ('{to:yyyy-MM-dd} 00:00:00+00')
                """, ct);
#pragma warning restore EF1002
        }

        await tx.CommitAsync(ct);
    }
}
