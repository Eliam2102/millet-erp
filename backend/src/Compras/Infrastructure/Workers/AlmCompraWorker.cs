using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Compras.Application.Integration.AlmCompra;

namespace Millet.Compras.Infrastructure.Workers;

/// <summary>
/// Vacía la bandeja ERP → Almacenes cada <see cref="AlmCompraOptions.IntervaloSegundos"/>. Apagado por omisión
/// (<c>Integraciones:AlmCompra:Habilitado=false</c>): la bandeja se llena igual y nada se escribe en el destino.
/// </summary>
public sealed class AlmCompraWorker(
    IServiceScopeFactory scopes, IOptions<AlmCompraOptions> options, ILogger<AlmCompraWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Habilitado)
        {
            logger.LogInformation("[AlmCompraWorker] Deshabilitado (Integraciones:AlmCompra:Habilitado=false).");
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var tick = await scope.ServiceProvider.GetRequiredService<AlmCompraProcesador>().ProcesarAsync(stoppingToken);
                if (tick != new AlmCompraTick(0, 0, 0)) logger.LogInformation("[AlmCompraWorker] {Tick}", tick);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "[AlmCompraWorker] Error inesperado."); }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervaloSegundos)), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
