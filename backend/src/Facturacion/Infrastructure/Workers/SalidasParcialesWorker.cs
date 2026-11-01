using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Application.Pedidos.RegistrarSalidaFacturable;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Infrastructure.Workers;

/// <summary>
/// FAC-08: registra como salidas facturables las salidas parciales que entrega
/// <see cref="ISalidasParcialesReader"/>. Idempotente: una salida reentregada regresa la ya
/// registrada sin cambiar importes. En dev el lector es stub (vacío).
/// </summary>
public sealed class SalidasParcialesWorker(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<SalidasParcialesOptions> options,
    ILogger<SalidasParcialesWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.CurrentValue.Disabled)
        {
            logger.LogInformation("[SalidasParcialesWorker] Disabled — loop no inicia.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "[SalidasParcialesWorker] Error inesperado.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.CurrentValue.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    /// <summary>Internal para tests. Devuelve cuántas salidas nuevas registró.</summary>
    internal async Task<int> TickAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var reader = sp.GetRequiredService<ISalidasParcialesReader>();
        var sender = sp.GetRequiredService<ISender>();
        using var origin = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(SalidasParcialesWorker));
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();

        var salidas = await reader.LeerPendientesAsync(options.CurrentValue.BatchSize, cancellationToken);
        var nuevas = 0;
        foreach (var s in salidas)
        {
            try
            {
                var r = await sender.Send(new RegistrarSalidaFacturableCommand(null, s.NumeroPedidoAw, s.Origen, s.Referencia,
                    s.Lineas.Select(l => new SalidaFacturableLineaDto(l.Posicion, l.Cantidad)).ToList()), cancellationToken);
                if (!r.YaRegistrada) nuevas++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "[SalidasParcialesWorker] Salida {Referencia} del pedido {Pedido} no se registró",
                    s.Referencia, s.NumeroPedidoAw);
            }
        }

        if (nuevas > 0)
            logger.LogInformation("[SalidasParcialesWorker] Tick: {Nuevas}/{Total} salidas registradas.", nuevas, salidas.Count);
        return nuevas;
    }
}

public sealed class SalidasParcialesOptions
{
    public const string SectionName = "Facturacion:Workers:SalidasParciales";

    public bool Disabled { get; init; }
    public int IntervalSeconds { get; init; } = 300;
    public int BatchSize { get; init; } = 50;
}
