using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application.Integration;

namespace Millet.SharedKernel.Infrastructure.Outbox;

/// <summary>
/// M-046 · lleva un <see cref="ILocalIntegrationEventConsumer"/> a una suscripción real de Service Bus: la misma lógica que la
/// entrega local (C1.8) corre ahora con el topic del servidor. Payload corrupto (<see cref="JsonException"/>) va a dead-letter
/// con motivo <c>InvalidPayload</c> y la cola sigue; cualquier otra falla abandona el mensaje para reintento (5 entregas y luego
/// dead-letter por la suscripción). La idempotencia vive en el consumidor (decisión por pedido, fila de <c>aw_retorno</c>,
/// <c>eventos_procesados</c> de Contabilidad), no aquí.
/// </summary>
public sealed class ServiceBusConsumerWorker(
    ServiceBusClient client, ILocalIntegrationEventConsumer consumer, string subscription, ILogger logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var topic = consumer.TopicName;
        await using var processor = client.CreateProcessor(topic, subscription, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 1,
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5),
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
        });
        processor.ProcessMessageAsync += args => ProcesarAsync(args, args.CancellationToken);
        processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "{Consumidor} error. Source={Source} Entity={Entity}", consumer.GetType().Name, args.ErrorSource, args.EntityPath);
            return Task.CompletedTask;
        };

        try
        {
            await processor.StartProcessingAsync(stoppingToken);
            logger.LogInformation("{Consumidor} iniciado. Topic={Topic} Subscription={Sub}", consumer.GetType().Name, topic, subscription);
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            if (processor.IsProcessing) await processor.StopProcessingAsync(CancellationToken.None);
        }
    }

    internal async Task ProcesarAsync(ProcessMessageEventArgs args, CancellationToken ct)
    {
        var eventType = args.Message.Subject ?? string.Empty;
        var eventId = Guid.TryParse(args.Message.MessageId, out var id) ? id : Guid.NewGuid();
        try
        {
            await consumer.ConsumirLocalAsync(eventType, eventId, args.Message.Body.ToString(), ct);
            await args.CompleteMessageAsync(args.Message, ct);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Payload corrupto. EventType={EventType}. Dead-letter.", eventType);
            await args.DeadLetterMessageAsync(args.Message, "InvalidPayload", ex.Message, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error procesando {EventType} en {Consumidor}. Abandono para retry.", eventType, consumer.GetType().Name);
            await args.AbandonMessageAsync(args.Message, cancellationToken: ct);
        }
    }
}

public static class ServiceBusConsumerWorkerExtensions
{
    /// <summary>Registra <typeparamref name="T"/> (consumidor local) como worker de la suscripción dada. Solo en la rama con <c>ServiceBusClient</c>.</summary>
    public static IServiceCollection AddConsumidorServiceBus<T>(this IServiceCollection services, string subscription)
        where T : class, ILocalIntegrationEventConsumer
        // AddSingleton, no AddHostedService: este usa TryAddEnumerable y descarta los registros repetidos del mismo tipo (solo quedaría el primero).
        => services.AddSingleton<IHostedService>(sp => new ServiceBusConsumerWorker(
            sp.GetRequiredService<ServiceBusClient>(), ActivatorUtilities.CreateInstance<T>(sp), subscription, sp.GetRequiredService<ILogger<T>>()));
}
