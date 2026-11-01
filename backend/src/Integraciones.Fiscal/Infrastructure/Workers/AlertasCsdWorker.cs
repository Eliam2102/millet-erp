using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Fiscal.Application.Certificados;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Fiscal.Infrastructure.Workers;

/// <summary>
/// Avisos de vencimiento del CSD (F6.9): corre cada <c>IntervalHours</c>
/// (24 por omisión) y delega en <see cref="EvaluarAlertasCsdCommand"/>; el
/// command es lo que se prueba, el worker solo agenda. Mismo patrón que
/// <c>AlertaCarteraWorker</c>.
/// </summary>
public sealed class AlertasCsdWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AlertasCsdOptions> options,
    ILogger<AlertasCsdWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Disabled)
        {
            logger.LogInformation("AlertasCsdWorker deshabilitado por configuración.");
            return;
        }

        var intervalo = TimeSpan.FromHours(Math.Max(1, options.Value.IntervalHours));
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sp = scope.ServiceProvider;
                using var origin = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(AlertasCsdWorker));
                using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var enviados = await sp.GetRequiredService<IMediator>().Send(new EvaluarAlertasCsdCommand(), stoppingToken);
                logger.LogInformation("AlertasCsdWorker: corrida completada ({N} avisos).", enviados);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AlertasCsdWorker: error en la evaluación; reintenta en la siguiente corrida.");
            }

            try { await Task.Delay(intervalo, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
