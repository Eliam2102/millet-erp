using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Application.Workers;

/// <summary>
/// Barrido programado de clientes A+W (ADM-06). Solo se registra con
/// <c>ProgramacionHabilitada=true</c>. Un barrido por intervalo, secuencial (nunca solapa en el
/// proceso; entre instancias lo impide el índice único parcial). Reanuda un barrido vivo antes
/// de iniciar uno nuevo.
/// </summary>
public sealed class AwClientesSyncWorker : BackgroundService
{
    private const string Actor = "system:aw-clientes-sync";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AwClientesOptions _options;
    private readonly ILogger<AwClientesSyncWorker> _logger;
    private volatile bool _running;

    public bool IsRunning => _running;

    public AwClientesSyncWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<AwClientesOptions> options,
        ILogger<AwClientesSyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervaloProgramacionMinutos));
        _running = true;
        _logger.LogInformation("AwClientesSyncWorker iniciado. intervalo={Interval}min", interval.TotalMinutes);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SyncOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (AwClientesSyncException ex)
                {
                    // Flag apagada / origen sin configurar / barrido vivo en otra instancia: esperado, no es un fallo.
                    _logger.LogWarning("AwClientesSyncWorker: ciclo omitido ({Code}).", ex.Code);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AwClientesSyncWorker: ciclo falló; se reintenta en el siguiente intervalo.");
                }

                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            _running = false;
            _logger.LogInformation("AwClientesSyncWorker detenido.");
        }
    }

    internal async Task SyncOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        using var origin = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(AwClientesSyncWorker));
        using var _ = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        await sp.GetRequiredService<AwClientesSincronizador>().EjecutarProgramadoAsync(Actor, ct);
    }
}

/// <summary>Liveness del <see cref="AwClientesSyncWorker"/>.</summary>
public sealed class AwClientesSyncWorkerHealthCheck(AwClientesSyncWorker worker) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(worker.IsRunning
            ? HealthCheckResult.Healthy("AwClientesSyncWorker está corriendo.")
            : HealthCheckResult.Unhealthy("AwClientesSyncWorker NO está corriendo."));
}
