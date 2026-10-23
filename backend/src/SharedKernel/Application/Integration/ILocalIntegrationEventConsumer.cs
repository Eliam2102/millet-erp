namespace Millet.SharedKernel.Application.Integration;

/// <summary>
/// Consumidor de eventos de integración que puede recibir la entrega
/// <b>local, en el mismo proceso</b>, sin Azure Service Bus (C1.8 —
/// dispatch local para demo y desarrollo).
///
/// <para>
/// Lo implementan los listeners de Service Bus de cada módulo
/// (<c>*EventListenerWorker</c>) para que
/// <c>LocalDispatchIntegrationEventBusSender</c> les entregue directamente
/// las filas del outbox cuando <c>ServiceBus:EntregaLocal:Habilitada=true</c>
/// y no hay conexión a Service Bus. <b>Solo desarrollo y demo</b>: en QA y
/// producción la entrega siempre pasa por Service Bus.
/// </para>
///
/// <para>
/// <b>Contrato:</b> <see cref="ConsumirLocalAsync"/> debe <b>lanzar</b> si
/// el procesamiento falla (equivalente a abandonar el mensaje en Service
/// Bus): el <c>OutboxPublisherWorker</c> registra la falla y reintenta la
/// fila en el siguiente poll. La idempotencia del consumidor (marca en
/// <c>eventos_procesados</c>) evita aplicar dos veces el efecto en los
/// consumidores que ya habían terminado.
/// </para>
/// </summary>
public interface ILocalIntegrationEventConsumer
{
    /// <summary>Topic de Service Bus al que el consumidor está suscrito.</summary>
    string TopicName { get; }

    /// <summary>
    /// Procesa un evento entregado localmente. <paramref name="eventId"/> es
    /// el <c>Id</c> de la fila del outbox (equivale al <c>MessageId</c> de
    /// Service Bus) y <paramref name="eventType"/> su <c>EventType</c>
    /// (equivale al <c>Subject</c>). Lanza si el procesamiento falla.
    /// </summary>
    Task ConsumirLocalAsync(
        string eventType,
        Guid eventId,
        string body,
        CancellationToken cancellationToken);
}
