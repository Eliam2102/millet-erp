using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.CostoPromedio;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Infrastructure.CostoPromedio;

/// <summary>
/// Publica <c>erp_costo_promedio</c> cada <see cref="ErpCostoPromedioOptions.IntervaloSegundos"/> (60 s por omisión). Apagado por
/// omisión (<c>Integraciones:CostoPromedio:Habilitado=false</c>). Un ciclo que falla se reintenta en el siguiente sin duplicar.
/// </summary>
public sealed class ErpCostoPromedioWorker(
    IServiceScopeFactory scopes, IOptions<ErpCostoPromedioOptions> options, ILogger<ErpCostoPromedioWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Habilitado)
        {
            logger.LogInformation("[ErpCostoPromedioWorker] Deshabilitado (Integraciones:CostoPromedio:Habilitado=false).");
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                await scope.ServiceProvider.GetRequiredService<ErpCostoPromedioPublicador>().PublicarAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "[ErpCostoPromedioWorker] Error al publicar; se reintenta en el siguiente ciclo."); }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervaloSegundos)), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
