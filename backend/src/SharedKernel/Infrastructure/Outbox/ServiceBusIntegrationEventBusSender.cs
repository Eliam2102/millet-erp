using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application.Integration;

namespace Millet.SharedKernel.Infrastructure.Outbox;

/// <summary>
/// Implementación real de <see cref="IIntegrationEventBusSender"/> con
/// Azure Service Bus (F6-PR2, ADR-0009). Singleton compartido por todos
/// los módulos del ERP — un único <see cref="ServiceBusClient"/> sobre
/// el namespace de Service Bus del proyecto. El topic destino lo
/// determina el caller (típicamente <c>OutboxPublisherWorker&lt;TDbContext&gt;</c>)
/// vía el parámetro <c>topicName</c> de <see cref="SendAsync"/>.
///
/// <para>
/// <b>Cache de senders:</b> <see cref="ServiceBusClient.CreateSender"/>
/// es barato pero NO inmediato (allocación + handshake amortizado).
/// Cachear instancias por <c>topicName</c> en un
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> evita recrearlas en
/// cada llamada — <c>ServiceBusSender</c> es thread-safe y reusable.
/// Las instancias se mantienen vivas mientras el <c>ServiceBusClient</c>
/// singleton lo esté (toda la vida del proceso).
/// </para>
/// <para>
/// <b>Convención de payload:</b> el cuerpo del mensaje es
/// <see cref="IntegrationEventOutboxEntry.Payload"/> (JSON serializado
/// del evento concreto). El consumer deserializa con el discriminador
/// <c>EventType</c> propagado como <c>Subject</c> y application property.
/// </para>
/// </summary>
public sealed class ServiceBusIntegrationEventBusSender : IIntegrationEventBusSender
{
    private readonly ServiceBusClient _client;
    private readonly ILogger<ServiceBusIntegrationEventBusSender> _logger;
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();

    public ServiceBusIntegrationEventBusSender(
        ServiceBusClient client,
        ILogger<ServiceBusIntegrationEventBusSender> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task SendAsync(
        IntegrationEventOutboxEntry entry,
        string topicName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(topicName))
        {
            throw new ArgumentException(
                "topicName es requerido para publicar a Service Bus. El caller debe pasar el topic name resuelto desde sus options.",
                nameof(topicName));
        }

        var sender = _senders.GetOrAdd(topicName, _client.CreateSender);

        var message = new ServiceBusMessage(BinaryData.FromString(entry.Payload))
        {
            ContentType = "application/json",
            Subject = entry.EventType,
            MessageId = entry.Id.ToString(),
        };
        message.ApplicationProperties["EventType"] = entry.EventType;
        message.ApplicationProperties["EmpresaId"] = entry.IntegrationEmpresaId.ToString();
        message.ApplicationProperties["OccurredAt"] = entry.OccurredAt.ToString("O");

        await sender.SendMessageAsync(message, cancellationToken);

        _logger.LogDebug(
            "Evento publicado a Service Bus. Topic={Topic} EventType={EventType} Id={Id}",
            topicName,
            entry.EventType,
            entry.Id);
    }
}
