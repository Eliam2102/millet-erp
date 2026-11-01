using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Catalogos.Application.Monedas;
using Millet.Compartido.Application.Ports;
using Millet.Compartido.Infrastructure.Banxico;
using Millet.SharedKernel.Application;

namespace Millet.Compartido.Infrastructure.Workers;

/// <summary>
/// K10.6: descarga el tipo de cambio de Banxico al arrancar y cada <see cref="BanxicoOptions.Interval"/>.
/// El comando es idempotente, así que correr de más no duplica ni cambia capturas manuales.
/// Si Banxico falla, o una captura manual difiere del valor de Banxico, avisa al Contador
/// General una vez al día por tipo de aviso. Sin token no corre.
/// </summary>
public sealed class TipoCambioBanxicoWorker(
    IServiceScopeFactory scopes, IClock clock, IOptions<BanxicoOptions> options, ILogger<TipoCambioBanxicoWorker> logger)
    : BackgroundService
{
    private DateOnly? _ultimaAlertaFalla;
    private DateOnly? _ultimaAlertaConflicto;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Disabled) return;
        if (!options.Value.TieneToken)
        {
            logger.LogWarning(
                "Sin {Seccion}:Token la descarga del tipo de cambio de Banxico no corre; los TC se capturan a mano.",
                BanxicoOptions.SectionName);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CorrerUnaVezAsync(stoppingToken);
                await Task.Delay(options.Value.Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    /// <summary>Una descarga con sus avisos. No lanza: las fallas se registran y se avisan.</summary>
    public async Task CorrerUnaVezAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        using var origen = sp.GetService<IAuditOriginContext>()?.SetOrigin(nameof(TipoCambioBanxicoWorker));
        var alertas = sp.GetRequiredService<IAlertaTipoCambioPort>();
        var hoy = FechaContable.HoyLocal(clock.UtcNow);

        DescargaTipoCambioResultado resultado;
        try
        {
            resultado = await sp.GetRequiredService<ISender>().Send(new DescargarTipoCambioBanxicoCommand(), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateException ex)
        {
            // Otra instancia guardó las mismas fechas al mismo tiempo; la siguiente corrida ya las ve.
            logger.LogWarning(ex, "La descarga de Banxico chocó con otra al guardar; se reintentará.");
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo descargar el tipo de cambio de Banxico; se reintentará.");
            if (_ultimaAlertaFalla == hoy) return;
            _ultimaAlertaFalla = hoy;
            await alertas.NotificarAsync(new AlertaTipoCambio(AlertaTipoCambio.ContadorGeneral,
                "No se pudo descargar el tipo de cambio de Banxico",
                $"{ex.Message} Se reintentará cada {options.Value.Interval.TotalMinutes:0} min; mientras tanto capture el TC a mano."), ct);
            return;
        }

        var conflictos = resultado.Monedas.Where(m => m.Conflictos.Count > 0).ToList();
        if (conflictos.Count == 0 || _ultimaAlertaConflicto == hoy) return;
        _ultimaAlertaConflicto = hoy;
        await alertas.NotificarAsync(new AlertaTipoCambio(AlertaTipoCambio.ContadorGeneral,
            "Tipo de cambio capturado a mano distinto del de Banxico",
            "Se conservó la captura manual en: " + string.Join("; ", conflictos.Select(m =>
                $"{m.Moneda} {string.Join(", ", m.Conflictos.Select(f => f.ToString("dd/MM/yyyy")))}")) + "."), ct);
    }
}
