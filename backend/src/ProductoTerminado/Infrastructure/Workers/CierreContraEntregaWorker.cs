using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.ProductoTerminado.Application.Liquidacion;
using Millet.SharedKernel.Application;

namespace Millet.ProductoTerminado.Infrastructure.Workers;

public sealed class CierreContraEntregaOptions
{
    public const string SectionName = "ProductoTerminado:CierreContraEntrega";

    public bool Disabled { get; set; }

    /// <summary>
    /// Regla de salida 5: días hábiles (lunes a sábado) para liquidar la hoja, por clave de sucursal; la que no aparece tiene 1. Conkal: 2.
    /// Se configura por ambiente porque la clave de Conkal no es la misma en el seed que en producción.
    /// </summary>
    public Dictionary<string, int> DiasLiquidacionPorSucursal { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int DiasLiquidacion(string? claveSucursal) =>
        claveSucursal is not null && DiasLiquidacionPorSucursal.TryGetValue(claveSucursal, out var dias) && dias > 0 ? dias : 1;
}

/// <summary>
/// Alerta de liquidación (PT5.7, regla de salida 5): cada 30 minutos avisa a CxC de la contra entrega que sigue sin liquidar cuando la hoja
/// cumple 12 horas hábiles en la zona horaria de su sucursal (antes: a las 20:00 de Ciudad de México para todas). El aviso es idempotente
/// por albarán, así que repetir la corrida no duplica nada; la lógica vive en <see cref="CerrarDiaContraEntregaCommand"/>.
/// </summary>
public sealed class CierreContraEntregaWorker(IServiceScopeFactory scopes, IOptions<CierreContraEntregaOptions> options, ILogger<CierreContraEntregaWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Disabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                using var origin = scope.ServiceProvider.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(CierreContraEntregaWorker));
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var avisos = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new CerrarDiaContraEntregaCommand(PorAlerta: true), stoppingToken);
                if (avisos > 0) logger.LogInformation("Alerta de liquidación: {Avisos} contra entrega sin liquidar avisada(s) a CxC.", avisos);
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
