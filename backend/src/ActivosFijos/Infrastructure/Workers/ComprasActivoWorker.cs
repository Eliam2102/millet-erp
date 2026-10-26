using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.ActivosFijos.Application;
using Millet.Almacen.Application.Integration;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;
namespace Millet.ActivosFijos.Infrastructure.Workers;
public sealed class ComprasActivoWorker(ServiceBusClient client, IServiceScopeFactory scopes, ILogger<ComprasActivoWorker> logger) : BackgroundService
{
    public const string TopicAlmacen = "almacen-events";
    public const string TopicCxp = "cuentas-por-pagar-events";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var recepciones = client.CreateProcessor(TopicAlmacen, "activos-fijos-almacen", new ServiceBusProcessorOptions { AutoCompleteMessages = false, MaxConcurrentCalls = 1 });
        await using var facturas = client.CreateProcessor(TopicCxp, "activos-fijos-cxp", new ServiceBusProcessorOptions { AutoCompleteMessages = false, MaxConcurrentCalls = 1 });
        recepciones.ProcessMessageAsync += ProcesarAsync; facturas.ProcessMessageAsync += ProcesarAsync;
        recepciones.ProcessErrorAsync += ErrorAsync; facturas.ProcessErrorAsync += ErrorAsync;
        await recepciones.StartProcessingAsync(stoppingToken); await facturas.StartProcessingAsync(stoppingToken);
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { await recepciones.StopProcessingAsync(CancellationToken.None); await facturas.StopProcessingAsync(CancellationToken.None); }
    }
    private Task ErrorAsync(ProcessErrorEventArgs e) { logger.LogError(e.Exception, "Error del consumidor de compras de activos fijos"); return Task.CompletedTask; }
    private Task ProcesarAsync(ProcessMessageEventArgs e) => ProcessAsync(e.Message.Subject ?? string.Empty, e.Message.Body.ToString(), e, e.CancellationToken);

    /// <summary>
    /// Con Service Bus: completa al terminar, dead-letter si el payload es inválido y deja
    /// propagar cualquier otra falla (el processor abandona el mensaje para reintento).
    /// Sin <paramref name="smbArgs"/> (entrega local C1.8): toda falla salvo payload
    /// inválido se propaga para que el outbox reintente la fila.
    /// </summary>
    internal Task ProcessAsync(string eventType, string body, ProcessMessageEventArgs? smbArgs, CancellationToken ct)
        => ProcessAsync(scopes, logger, eventType, body, smbArgs, ct);

    // Estático para que los consumidores locales lo usen sin instanciar el BackgroundService (disposable).
    internal static async Task ProcessAsync(IServiceScopeFactory scopes, ILogger logger, string eventType, string body, ProcessMessageEventArgs? smbArgs, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        using var empresa = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var consumer = scope.ServiceProvider.GetRequiredService<CompraActivoConsumer>();
        try
        {
            if (eventType == "almacen.oc_recepcion.registrada.v1")
                await consumer.RecibirAsync(JsonSerializer.Deserialize<OcRecepcionRegistradaIntegrationEvent>(body, Json) ?? throw new JsonException(), ct);
            else if (eventType == "almacen.recepcion_activo.anulada.v1")
                await consumer.AnularAsync(JsonSerializer.Deserialize<RecepcionActivoAnuladaIntegrationEvent>(body, Json) ?? throw new JsonException(), ct);
            else if (eventType == "cuentas_por_pagar.factura.registrada.v1")
                await consumer.ConciliarAsync(JsonSerializer.Deserialize<FacturaConciliadaActivo>(body, Json) ?? throw new JsonException(), ct);
            if (smbArgs is not null) await smbArgs.CompleteMessageAsync(smbArgs.Message, ct);
        }
        catch (JsonException ex)
        {
            if (smbArgs is not null) await smbArgs.DeadLetterMessageAsync(smbArgs.Message, "AF_EVENTO_INVALIDO", cancellationToken: ct);
            else logger.LogError(ex, "Payload inválido en entrega local. EventType={EventType}. Se descarta.", eventType);
        }
    }
}

/// <summary>
/// C1.8 — entrega local sin Service Bus de <c>almacen-events</c> (recepciones) a
/// <see cref="ComprasActivoWorker"/>. La interfaz admite un solo topic, así que el
/// worker se expone con un consumidor local por topic.
/// </summary>
public sealed class ComprasActivoAlmacenLocalConsumer(IServiceScopeFactory scopes, ILogger<ComprasActivoWorker> logger) : ILocalIntegrationEventConsumer
{
    public string TopicName => ComprasActivoWorker.TopicAlmacen;
    public Task ConsumirLocalAsync(string eventType, Guid eventId, string body, CancellationToken cancellationToken)
        => ComprasActivoWorker.ProcessAsync(scopes, logger, eventType, body, smbArgs: null, cancellationToken);
}

/// <summary>C1.8 — entrega local de <c>cuentas-por-pagar-events</c> (facturas) a <see cref="ComprasActivoWorker"/>.</summary>
public sealed class ComprasActivoCxpLocalConsumer(IServiceScopeFactory scopes, ILogger<ComprasActivoWorker> logger) : ILocalIntegrationEventConsumer
{
    public string TopicName => ComprasActivoWorker.TopicCxp;
    public Task ConsumirLocalAsync(string eventType, Guid eventId, string body, CancellationToken cancellationToken)
        => ComprasActivoWorker.ProcessAsync(scopes, logger, eventType, body, smbArgs: null, cancellationToken);
}
