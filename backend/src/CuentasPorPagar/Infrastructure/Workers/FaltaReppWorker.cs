using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.SharedKernel.Application;
using Millet.CuentasPorPagar.Application.FacturaProveedor.RevisionRepp;
namespace Millet.CuentasPorPagar.Infrastructure.Workers;
public sealed class FaltaReppWorker(IServiceScopeFactory scopes, IOptionsMonitor<WorkerSchedulingOptions> options, ILogger<FaltaReppWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.CurrentValue.Disabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                using var origin = scope.ServiceProvider.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(FaltaReppWorker));
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RevisarFaltaReppCommand(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError(e, "Error al revisar complementos pendientes de proveedor."); }
            try { await Task.Delay(TimeSpan.FromDays(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
