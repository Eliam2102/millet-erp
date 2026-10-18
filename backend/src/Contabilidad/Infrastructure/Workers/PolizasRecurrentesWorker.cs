using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Contabilidad.Application.Polizas;
using Millet.SharedKernel.Application;

namespace Millet.Contabilidad.Infrastructure.Workers;

/// <summary>Opciones del generador de pólizas recurrentes (K10.4), sección <c>Contabilidad:Recurrentes</c>.</summary>
public sealed class RecurrentesOpciones
{
    public const string Seccion = "Contabilidad:Recurrentes";
    public bool Deshabilitado { get; set; }
    public int IntervaloMinutos { get; set; } = 60;
}

/// <summary>
/// Genera cada <c>IntervaloMinutos</c> lo que toca de las plantillas recurrentes activas (CA10.6) y sus reversas automáticas del día
/// 1 (K10.4-b), con <see cref="GeneradorRecurrentes"/> (idempotente: repetir la corrida no duplica). Corre sin request: levanta el
/// filtro de empresa y cada póliza toma la empresa de su plantilla. Lo que no se puede generar queda pendiente para la siguiente corrida.
/// </summary>
public sealed class PolizasRecurrentesWorker(IServiceScopeFactory scopeFactory, IOptions<RecurrentesOpciones> opciones, ILogger<PolizasRecurrentesWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (opciones.Value.Deshabilitado)
        {
            logger.LogInformation("PolizasRecurrentesWorker deshabilitado por configuración.");
            return;
        }
        var intervalo = TimeSpan.FromMinutes(Math.Max(1, opciones.Value.IntervaloMinutos));
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sp = scope.ServiceProvider;
                using var origen = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(PolizasRecurrentesWorker));
                using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var hoy = FechaContable.HoyLocal(sp.GetRequiredService<IClock>().UtcNow);
                var r = await sp.GetRequiredService<GeneradorRecurrentes>().GenerarAsync(hoy, null, stoppingToken);
                if (r.Generadas.Count > 0 || r.Pendientes.Count > 0)
                    logger.LogInformation("PolizasRecurrentesWorker: {Generadas} generadas, {Pendientes} pendientes.", r.Generadas.Count, r.Pendientes.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "PolizasRecurrentesWorker: error en la corrida; reintenta en la siguiente.");
            }
            try { await Task.Delay(intervalo, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
