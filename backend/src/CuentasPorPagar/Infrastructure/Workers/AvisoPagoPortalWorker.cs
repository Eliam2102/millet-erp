using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.CuentasPorPagar.Application.Portal;
using Millet.SharedKernel.Application;
namespace Millet.CuentasPorPagar.Infrastructure.Workers;

/// <summary>Cada 5 minutos envía los avisos de pago pendientes a los correos autorizados del portal (P8.3).</summary>
public sealed class AvisoPagoPortalWorker(IServiceScopeFactory scopes, IOptionsMonitor<WorkerSchedulingOptions> options, ILogger<AvisoPagoPortalWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.CurrentValue.Disabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                using var origin = scope.ServiceProvider.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(AvisoPagoPortalWorker));
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                await scope.ServiceProvider.GetRequiredService<ISender>().Send(new EnviarAvisosPagoPortalCommand(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError(e, "Error al enviar avisos de pago del portal de proveedores."); }
            try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
