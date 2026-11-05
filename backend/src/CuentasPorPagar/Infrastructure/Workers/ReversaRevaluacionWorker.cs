using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.CuentasPorPagar.Application.Revaluacion;
using Millet.SharedKernel.Application;
namespace Millet.CuentasPorPagar.Infrastructure.Workers;
/// <summary>CXP-12: cada día publica la reversa de las revaluaciones de cierre cuyo día 1 ya llegó.</summary>
public sealed class ReversaRevaluacionWorker(IServiceScopeFactory scopes, IOptionsMonitor<WorkerSchedulingOptions> options, ILogger<ReversaRevaluacionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.CurrentValue.Disabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                using var origin = scope.ServiceProvider.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(ReversaRevaluacionWorker));
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                await scope.ServiceProvider.GetRequiredService<ISender>().Send(new PublicarReversasRevaluacionCommand(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError(e, "Error al publicar la reversa de la revaluación de cierre."); }
            try { await Task.Delay(TimeSpan.FromHours(6), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
