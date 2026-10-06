using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Cambios;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Application.Workers;

/// <summary>
/// Sincronización continua A+W → ERP por CDC. Solo se registra con <c>IntegracionesAw:Cambios:Habilitado=true</c>.
/// Un fallo de una entidad no detiene a la otra ni al worker; se reintenta en el siguiente intervalo.
/// ponytail: single-host (como el dispatcher de clientes); con réplicas dos workers releen los mismos cambios (idempotente, duplica lecturas).
/// </summary>
public sealed class AwCambiosSyncWorker(
    IServiceScopeFactory scopeFactory, IOptions<AwCambiosOptions> options, ILogger<AwCambiosSyncWorker> logger) : BackgroundService
{
    private volatile bool _running;
    public bool IsRunning => _running;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        var intervalo = TimeSpan.FromSeconds(Math.Max(5, o.IntervaloSegundos));
        _running = true;
        logger.LogInformation("AwCambiosSyncWorker iniciado. intervalo={Intervalo}s", intervalo.TotalSeconds);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (o.Clientes) await CicloSeguroAsync(AwEntidadCambio.Cliente, stoppingToken);
                if (o.Productos) await CicloSeguroAsync(AwEntidadCambio.Producto, stoppingToken);
                try { await Task.Delay(intervalo, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
        finally
        {
            _running = false;
            logger.LogInformation("AwCambiosSyncWorker detenido.");
        }
    }

    private async Task CicloSeguroAsync(AwEntidadCambio entidad, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            using var origin = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(AwCambiosSyncWorker));
            using var _ = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var r = await sp.GetRequiredService<AwCambiosAplicador>().CicloAsync(entidad, ct);
            if (r.Upserts + r.Eliminados > 0 || r.BarridoCompleto)
                logger.LogInformation("CDC {Entidad}: {Upserts} cambios aplicados, {Eliminados} borrados informados, barrido={Barrido}.",
                    entidad, r.Upserts, r.Eliminados, r.BarridoCompleto);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "AwCambiosSyncWorker: ciclo de {Entidad} falló; se reintenta en el siguiente intervalo.", entidad);
        }
    }
}

/// <summary>Liveness del <see cref="AwCambiosSyncWorker"/>.</summary>
public sealed class AwCambiosSyncWorkerHealthCheck(AwCambiosSyncWorker worker) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(worker.IsRunning
            ? HealthCheckResult.Healthy("AwCambiosSyncWorker está corriendo.")
            : HealthCheckResult.Unhealthy("AwCambiosSyncWorker NO está corriendo."));
}
