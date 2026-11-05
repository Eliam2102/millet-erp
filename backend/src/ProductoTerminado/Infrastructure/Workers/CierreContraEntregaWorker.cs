using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Application.Liquidacion;
using Millet.SharedKernel.Application;

namespace Millet.ProductoTerminado.Infrastructure.Workers;

public sealed class CierreContraEntregaOptions
{
    public const string SectionName = "ProductoTerminado:CierreContraEntrega";

    public bool Disabled { get; set; }
    /// <summary>Hora (Ciudad de México) desde la que se considera cerrado el día de caja. Supuesto: 20:00, a validar con Caja.</summary>
    public int Hora { get; set; } = 20;
}

/// <summary>
/// Cierre del día (PT5.7): cada 30 minutos, pasada la hora de cierre, avisa a CxC de la contra entrega que sigue sin liquidar. El aviso es idempotente
/// por albarán, así que repetir la corrida no duplica nada; la lógica vive en <see cref="CerrarDiaContraEntregaCommand"/>.
/// </summary>
public sealed class CierreContraEntregaWorker(IServiceScopeFactory scopes, IClock clock, IOptions<CierreContraEntregaOptions> options, ILogger<CierreContraEntregaWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Disabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        do
        {
            try
            {
                if (HoraAw.HoraLocal(clock.UtcNow) >= options.Value.Hora)
                {
                    using var scope = scopes.CreateScope();
                    using var origin = scope.ServiceProvider.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(CierreContraEntregaWorker));
                    using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                    var avisos = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new CerrarDiaContraEntregaCommand(), stoppingToken);
                    if (avisos > 0) logger.LogInformation("Cierre del día: {Avisos} contra entrega sin liquidar avisada(s) a CxC.", avisos);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Cierre del día de contra entrega: error, reintenta en la siguiente corrida."); }
        } while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
