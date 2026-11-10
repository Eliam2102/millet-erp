using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Compartido.Application.TableroIntegraciones;
using Millet.SharedKernel.Application;

namespace Millet.Compartido.Infrastructure.Workers;

/// <summary>
/// I13.6: cada <see cref="TableroIntegracionesOptions.IntervaloSegundos"/> revisa
/// todas las integraciones del tablero, abre o cierra sus alertas y avisa por
/// correo. Lee las fuentes de todas las empresas (bypass), porque una cola
/// detenida es del sistema, no de un usuario.
/// </summary>
public sealed class AlertasIntegracionWorker(
    IServiceScopeFactory scopes, IOptionsMonitor<TableroIntegracionesOptions> opciones, ILogger<AlertasIntegracionWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (opciones.CurrentValue.AlertasDeshabilitadas)
        {
            logger.LogInformation("[I13.6] Alertas del tablero de integraciones deshabilitadas por configuración.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var r = await scope.ServiceProvider.GetRequiredService<TableroIntegracionesServicio>()
                    .EvaluarAlertasAsync(stoppingToken);
                if (r.Abiertas > 0 || r.Cerradas > 0)
                    logger.LogInformation("[I13.6] Alertas de integración: {Abiertas} abiertas, {Cerradas} recuperadas, {Correos} correos.",
                        r.Abiertas, r.Cerradas, r.CorreosEnviados);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "[I13.6] Falló la evaluación de alertas de integración; se reintenta en el siguiente ciclo.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(15, opciones.CurrentValue.IntervaloSegundos)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
