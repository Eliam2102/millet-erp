using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Idempotency;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.SharedKernel.Infrastructure.Idempotency;

/// <summary>
/// <c>BackgroundService</c> que purga filas de <c>core.idempotency_keys</c>
/// según la política de retención del ADR-0020 §"Política de retención":
/// <c>processing</c> &gt; 1h y <c>completed</c>/<c>failed</c> &gt; 24h.
///
/// <para>
/// Usa <c>pg_try_advisory_xact_lock</c> con un lock id constante para
/// asegurar que solo una réplica corra el cleanup en un ciclo dado. Si el
/// lock no se obtiene, el ciclo se salta — la próxima ventana lo intenta
/// de nuevo. Patrón distinto al outbox publisher (FOR UPDATE SKIP LOCKED
/// por mensaje): aquí queremos exclusión total entre instancias durante
/// el DELETE.
/// </para>
/// </summary>
public sealed class IdempotencyKeysCleanupJob : BackgroundService
{
    /// <summary>
    /// Lock id arbitrario pero estable. <c>pg_try_advisory_xact_lock</c>
    /// usa <c>bigint</c>; este valor nunca colisiona con otros locks
    /// declarados en el sistema (los demás usan ids derivados de hash).
    /// </summary>
    private const long AdvisoryLockId = 7_390_001L;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IdempotencyOptions _options;
    private readonly ILogger<IdempotencyKeysCleanupJob> _logger;

    public IdempotencyKeysCleanupJob(
        IServiceScopeFactory scopeFactory,
        IOptions<IdempotencyOptions> options,
        ILogger<IdempotencyKeysCleanupJob> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Disabled)
        {
            _logger.LogInformation("IdempotencyKeysCleanupJob deshabilitado (Idempotency:Disabled=true).");
            return;
        }

        _logger.LogInformation(
            "IdempotencyKeysCleanupJob iniciado. Interval={Interval} ProcessingRetention={Proc} CompletedRetention={Comp}",
            _options.CleanupInterval, _options.ProcessingRetention, _options.CompletedRetention);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en IdempotencyKeysCleanupJob. Continuando tras intervalo.");
            }

            try
            {
                await Task.Delay(_options.CleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Ejecuta un ciclo de cleanup. Internal para que tests puedan
    /// dispararlo sin esperar el intervalo.
    /// </summary>
    internal async Task<int> CleanupOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<CoreDbContext>();
        var clock = sp.GetRequiredService<IClock>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Advisory lock TX-scoped: se libera en commit/rollback.
        var locked = await db.Database
            .SqlQueryRaw<bool>("SELECT pg_try_advisory_xact_lock({0}) AS \"Value\"", AdvisoryLockId)
            .SingleAsync(ct);

        if (!locked)
        {
            _logger.LogDebug("Otra instancia ya está corriendo el cleanup. Skipping este ciclo.");
            await tx.CommitAsync(ct);
            return 0;
        }

        var now = clock.UtcNow;
        var processingCutoff = now - _options.ProcessingRetention;
        var completedCutoff = now - _options.CompletedRetention;

        // Borrado en una sola query con OR; Postgres aprovecha el index
        // (status, created_at).
        var deleted = await db.IdempotencyKeys
            .Where(x =>
                (x.Status == IdempotencyStatuses.Processing && x.CreatedAt < processingCutoff)
                || ((x.Status == IdempotencyStatuses.Completed || x.Status == IdempotencyStatuses.Failed)
                    && x.CreatedAt < completedCutoff))
            .ExecuteDeleteAsync(ct);

        await tx.CommitAsync(ct);

        if (deleted > 0)
        {
            _logger.LogInformation(
                "IdempotencyKeysCleanupJob purgó {Count} keys (processing<={Proc:o} completed<={Comp:o}).",
                deleted, processingCutoff, completedCutoff);
        }

        return deleted;
    }
}
