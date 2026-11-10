using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.SharedKernel.Application;
using Millet.Integraciones.Aw.Infrastructure.CostoPromedio;

namespace Millet.Integraciones.Aw.Infrastructure.CostoPromedio.Workers;

/// <summary>
/// Publica el costo promedio del ERP a la app de Almacenes (M-447) cada <see cref="CostoPromedioOptions.IntervaloSegundos"/>.
/// Apagado por omisión (<c>Integraciones:CostoPromedio:Habilitado=false</c>).
/// </summary>
public sealed class CostoPromedioWorker(
    IServiceScopeFactory scopes, IOptions<CostoPromedioOptions> options, ILogger<CostoPromedioWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Habilitado)
        {
            logger.LogInformation("[CostoPromedioWorker] Deshabilitado (Integraciones:CostoPromedio:Habilitado=false).");
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                // Proceso de fondo sin empresa de sesión: lee los saldos de todas las empresas (permitido en Workers/, BypassUsageGuardTest).
                using var origen = scope.ServiceProvider.GetService<IAuditOriginContext>()?.SetOrigin(nameof(CostoPromedioWorker));
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var actuales = await scope.ServiceProvider.GetRequiredService<CostoPromedioLector>().LeerAsync(stoppingToken);
                var n = await scope.ServiceProvider.GetRequiredService<CostoPromedioPublicador>().PublicarAsync(actuales, stoppingToken);
                if (n > 0) logger.LogInformation("[CostoPromedioWorker] {N} costos publicados.", n);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "[CostoPromedioWorker] Error inesperado."); }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervaloSegundos)), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
