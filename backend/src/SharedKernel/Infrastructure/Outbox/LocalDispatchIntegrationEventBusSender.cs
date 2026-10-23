using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application.Integration;

namespace Millet.SharedKernel.Infrastructure.Outbox;

/// <summary>
/// <see cref="IIntegrationEventBusSender"/> que entrega los eventos del
/// outbox <b>en el mismo proceso</b> a los
/// <see cref="ILocalIntegrationEventConsumer"/> suscritos al topic, sin
/// Azure Service Bus (C1.8 — dispatch local para demo y desarrollo).
///
/// <para>
/// Se registra solo cuando <c>ServiceBus:EntregaLocal:Habilitada=true</c>,
/// no hay conexión a Service Bus y el ambiente es Development (ver
/// <c>EntregaLocalIntegrationEventsExtensions</c> en <c>Millet.Api</c>).
/// <b>NO usar en QA/Prod.</b>
/// </para>
///
/// <para>
/// <b>Semántica:</b> invoca a los consumidores del topic en secuencia con
/// <c>eventId = entry.Id</c>, <c>eventType = entry.EventType</c> y
/// <c>body = entry.Payload</c> (mismo mapeo que
/// <see cref="ServiceBusIntegrationEventBusSender"/>: MessageId, Subject y
/// cuerpo). Si un consumidor lanza, la excepción se propaga: el
/// <c>OutboxPublisherWorker</c> registra la falla y reintenta la fila; los
/// consumidores que ya habían terminado no repiten el efecto gracias a su
/// marca de idempotencia (<c>eventos_procesados</c>). Un topic sin
/// consumidores se da por entregado.
/// </para>
/// </summary>
public sealed class LocalDispatchIntegrationEventBusSender : IIntegrationEventBusSender
{
    private readonly IReadOnlyList<ILocalIntegrationEventConsumer> _consumers;
    private readonly ILogger<LocalDispatchIntegrationEventBusSender> _logger;

    public LocalDispatchIntegrationEventBusSender(
        IEnumerable<ILocalIntegrationEventConsumer> consumers,
        ILogger<LocalDispatchIntegrationEventBusSender> logger)
    {
        _consumers = consumers.ToList();
        _logger = logger;
    }

    public async Task SendAsync(
        IntegrationEventOutboxEntry entry,
        string topicName,
        CancellationToken cancellationToken)
    {
        var suscritos = _consumers
            .Where(c => string.Equals(c.TopicName, topicName, StringComparison.Ordinal))
            .ToList();

        if (suscritos.Count == 0)
        {
            _logger.LogDebug(
                "[EntregaLocal] Topic {Topic} sin consumidores locales; evento {EventType} Id={Id} se da por entregado.",
                topicName, entry.EventType, entry.Id);
            return;
        }

        foreach (var consumidor in suscritos)
        {
            await consumidor.ConsumirLocalAsync(entry.EventType, entry.Id, entry.Payload, cancellationToken);
            _logger.LogInformation(
                "[EntregaLocal] Evento {EventType} Id={Id} entregado a {Consumidor} (topic {Topic}).",
                entry.EventType, entry.Id, consumidor.GetType().Name, topicName);
        }
    }
}
