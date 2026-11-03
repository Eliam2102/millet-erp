using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.ProductoTerminado.Application.Liberacion;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Infrastructure.PiezasPt;

/// <summary>
/// Barrido de pedidos retenidos de PT (PT5.2): cada <c>BarridoMinutos</c> reevalúa saldo y REPARTO, porque Facturación aún no publica el
/// cambio de saldo (cuando lo haga, se reemplaza por la suscripción). Idempotente: un pedido ya liberado no cambia ni publica de nuevo.
/// Apagado con la misma bandera que la entrada de piezas.
/// </summary>
public sealed class PiezasPtLiberacionWorker(IServiceScopeFactory scopes, IOptions<PiezasPtOptions> options, ILogger<PiezasPtLiberacionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.EntradaHabilitada) return;
        while (!stoppingToken.IsCancellationRequested)
            try { await Task.Delay(TimeSpan.FromMinutes(options.Value.BarridoMinutos), stoppingToken); await CorrerUnaVezAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Barrido de pedidos retenidos de PT interrumpido; se reintentará en el siguiente ciclo."); }
    }

    public async Task<int> CorrerUnaVezAsync(CancellationToken ct)
    {
        if (!options.Value.EntradaHabilitada) return 0;
        using var scope = scopes.CreateScope(); var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        using var origen = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(PiezasPtLiberacionWorker));
        using var correlacion = sp.GetRequiredService<IAuditCorrelationContext>().Begin(Guid.CreateVersion7());
        return await sp.GetRequiredService<IMediator>().Send(new BarrerRetenidosCommand(), ct);
    }
}
