using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Application.Workers;

/// <summary>
/// Ejecuta los barridos de clientes A+W (ADM-06) que el API o el worker de programación dejaron
/// Pendiente/EnCurso. Reanuda tras una caída (un barrido EnCurso continúa desde su cursor).
/// ponytail: asume un solo host; con réplicas dos dispatchers podrían tomar el mismo barrido
/// (idempotente pero duplica lecturas). Si se escala, añadir claim explícito (lease/columna).
/// </summary>
public sealed class AwClientesEjecucionDispatcher : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AwClientesOptions _options;
    private readonly ILogger<AwClientesEjecucionDispatcher> _logger;
    private volatile bool _running;

    public bool IsRunning => _running;

    public AwClientesEjecucionDispatcher(
        IServiceScopeFactory scopeFactory,
        IOptions<AwClientesOptions> options,
        ILogger<AwClientesEjecucionDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _running = true;
        _logger.LogInformation("AwClientesEjecucionDispatcher iniciado.");
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await DespacharAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Nunca mata el servicio; solo el tipo (sin mensaje: puede traer datos o secretos).
                    _logger.LogError("AwClientesEjecucionDispatcher: ciclo falló ({Tipo}); se reintenta.", ex.GetType().Name);
                }

                try
                {
                    await Task.Delay(Intervalo, stoppingToken);
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
            _logger.LogInformation("AwClientesEjecucionDispatcher detenido.");
        }
    }

    internal async Task DespacharAsync(CancellationToken ct)
    {
        List<Guid> ids;
        using (var scope = _scopeFactory.CreateScope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
        {
            var origen = _options.Origen.ToString();
            ids = await scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>().ClientesEjecuciones
                .Where(e => e.Origen == origen && e.Tipo == AwClientesEjecucionTipo.Barrido
                    && (e.Estado == AwClientesEjecucionEstado.Pendiente || e.Estado == AwClientesEjecucionEstado.EnCurso))
                .OrderBy(e => e.IniciadaEnUtc)
                .Select(e => e.Id)
                .ToListAsync(ct);
        }

        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sp = scope.ServiceProvider;
                using var origin = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(AwClientesEjecucionDispatcher));
                using var _ = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                await sp.GetRequiredService<AwClientesSincronizador>().EjecutarAsync(id, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (AwClientesSyncException ex)
            {
                _logger.LogWarning("AwClientesEjecucionDispatcher: ejecución {EjecucionId} omitida ({Code}).", id, ex.Code);
            }
            catch (Exception ex)
            {
                _logger.LogError("AwClientesEjecucionDispatcher: ejecución {EjecucionId} falló ({Tipo}).", id, ex.GetType().Name);
            }
        }
    }
}

/// <summary>Liveness del <see cref="AwClientesEjecucionDispatcher"/>.</summary>
public sealed class AwClientesEjecucionDispatcherHealthCheck(AwClientesEjecucionDispatcher worker) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(worker.IsRunning
            ? HealthCheckResult.Healthy("AwClientesEjecucionDispatcher está corriendo.")
            : HealthCheckResult.Unhealthy("AwClientesEjecucionDispatcher NO está corriendo."));
}
