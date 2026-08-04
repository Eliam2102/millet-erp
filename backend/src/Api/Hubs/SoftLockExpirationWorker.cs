using Microsoft.Extensions.Options;
using Millet.SharedKernel.Application;

namespace Millet.Api.Hubs;

/// <summary>
/// BackgroundService que escanea periódicamente los soft locks y expira
/// los que llevan más de <see cref="SoftLockOptions.HeartbeatExpirationSeconds"/>
/// sin heartbeat. Cuando releasa una entry, <see cref="ISoftLockManager"/>
/// emite el broadcast <c>userPresence</c> al grupo de la empresa, así que
/// el FE ve correctamente cuando un peer se desconectó silenciosamente
/// (cierra browser sin llamar <c>LeaveResource</c>).
///
/// Sweep interval &lt; TTL: en condiciones normales una entry vive como
/// mucho TTL + sweep antes de ser barrida (default: 90s + 30s = 120s peor caso).
/// </summary>
public sealed class SoftLockExpirationWorker : BackgroundService
{
    private readonly ISoftLockManager _manager;
    private readonly IClock _clock;
    private readonly ILogger<SoftLockExpirationWorker> _logger;
    private readonly IOptionsMonitor<SoftLockOptions> _options;
    private readonly ComprasHubMeter _meter;

    public SoftLockExpirationWorker(
        ISoftLockManager manager,
        IClock clock,
        ILogger<SoftLockExpirationWorker> logger,
        IOptionsMonitor<SoftLockOptions> options,
        ComprasHubMeter meter)
    {
        _manager = manager;
        _clock = clock;
        _logger = logger;
        _options = options;
        _meter = meter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var opts = _options.CurrentValue;
            try
            {
                var ttl = TimeSpan.FromSeconds(opts.HeartbeatExpirationSeconds);
                var threshold = _clock.UtcNow - ttl;

                var expired = _manager.Snapshot()
                    .Where(e => e.LastSeenUtc < threshold)
                    .ToList();

                foreach (var entry in expired)
                {
                    _meter.SoftLockExpired.Add(1,
                        new KeyValuePair<string, object?>("compras.hub.empresa.id", entry.EmpresaId),
                        new KeyValuePair<string, object?>("compras.hub.entidad", entry.Entidad),
                        new KeyValuePair<string, object?>("compras.hub.modo", entry.Modo.ToString()));

                    await _manager.ReleaseAsync(entry.ConnectionId, stoppingToken);
                }

                if (expired.Count > 0)
                {
                    _logger.LogInformation(
                        "SoftLock sweep expired {Count} entries (TTL={TtlSeconds}s)",
                        expired.Count, opts.HeartbeatExpirationSeconds);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Apagado normal — salimos del loop.
                break;
            }
            catch (Exception ex)
            {
                // No queremos que un fallo aislado mate al worker — log y
                // seguir. Si el manager está roto, los siguientes sweeps
                // logearán también y será obvio en App Insights.
                _logger.LogError(ex, "SoftLock sweep failed");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(opts.SweepIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
