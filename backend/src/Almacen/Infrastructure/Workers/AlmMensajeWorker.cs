using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Almacen.Application.AppAlmacenes;

namespace Millet.Almacen.Infrastructure.Workers;

/// <summary>
/// Lee <c>alm_mensaje</c> cada <see cref="AlmMensajeOptions.IntervaloSegundos"/> (10 s por omisión; acordado con TI el
/// 5-nov). Apagado por omisión (<c>Integraciones:AlmMensaje:Habilitado=false</c>): sin tabla ni usuario no lee nada.
/// </summary>
public sealed class AlmMensajeWorker(
    AlmMensajeLector lector, IOptions<AlmMensajeOptions> options, ILogger<AlmMensajeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Habilitado)
        {
            logger.LogInformation("[AlmMensajeWorker] Deshabilitado (Integraciones:AlmMensaje:Habilitado=false).");
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var tick = await lector.ProcesarAsync(stoppingToken);
                if (tick != default) logger.LogInformation("[AlmMensajeWorker] {Tick}", tick);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "[AlmMensajeWorker] Error inesperado."); }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervaloSegundos)), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
