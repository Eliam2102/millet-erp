using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.ActivosFijos.Application;
using Millet.Almacen.Application.Integration;
using Millet.SharedKernel.Application;
namespace Millet.ActivosFijos.Infrastructure.Workers;
public sealed class ComprasActivoWorker(ServiceBusClient client, IServiceScopeFactory scopes, ILogger<ComprasActivoWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var recepciones = client.CreateProcessor("almacen-events", "activos-fijos-almacen", new ServiceBusProcessorOptions { AutoCompleteMessages = false, MaxConcurrentCalls = 1 });
        await using var facturas = client.CreateProcessor("cuentas-por-pagar-events", "activos-fijos-cxp", new ServiceBusProcessorOptions { AutoCompleteMessages = false, MaxConcurrentCalls = 1 });
        recepciones.ProcessMessageAsync += ProcesarAsync; facturas.ProcessMessageAsync += ProcesarAsync;
        recepciones.ProcessErrorAsync += ErrorAsync; facturas.ProcessErrorAsync += ErrorAsync;
        await recepciones.StartProcessingAsync(stoppingToken); await facturas.StartProcessingAsync(stoppingToken);
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { await recepciones.StopProcessingAsync(CancellationToken.None); await facturas.StopProcessingAsync(CancellationToken.None); }
    }
    private Task ErrorAsync(ProcessErrorEventArgs e) { logger.LogError(e.Exception, "Error del consumidor de compras de activos fijos"); return Task.CompletedTask; }
    private async Task ProcesarAsync(ProcessMessageEventArgs e)
    {
        using var scope = scopes.CreateScope();
        using var empresa = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var consumer = scope.ServiceProvider.GetRequiredService<CompraActivoConsumer>();
        try
        {
            if (e.Message.Subject == "almacen.oc_recepcion.registrada.v1")
                await consumer.RecibirAsync(JsonSerializer.Deserialize<OcRecepcionRegistradaIntegrationEvent>(e.Message.Body.ToString(), Json) ?? throw new JsonException(), e.CancellationToken);
            else if (e.Message.Subject == "almacen.recepcion_activo.anulada.v1")
                await consumer.AnularAsync(JsonSerializer.Deserialize<RecepcionActivoAnuladaIntegrationEvent>(e.Message.Body.ToString(), Json) ?? throw new JsonException(), e.CancellationToken);
            else if (e.Message.Subject == "cuentas_por_pagar.factura.registrada.v1")
                await consumer.ConciliarAsync(JsonSerializer.Deserialize<FacturaConciliadaActivo>(e.Message.Body.ToString(), Json) ?? throw new JsonException(), e.CancellationToken);
            await e.CompleteMessageAsync(e.Message, e.CancellationToken);
        }
        catch (JsonException) { await e.DeadLetterMessageAsync(e.Message, "AF_EVENTO_INVALIDO", cancellationToken: e.CancellationToken); }
    }
}
