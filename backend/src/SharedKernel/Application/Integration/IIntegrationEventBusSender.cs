using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.SharedKernel.Application.Integration;

/// <summary>
/// Abstrae el broker (Service Bus) al que el
/// <c>OutboxPublisherWorker</c> envía los eventos de integración
/// pendientes (F6-PR2, ADR-0009). Inyectable como dependencia del
/// worker para permitir tests unitarios sin Service Bus real.
///
/// <para>
/// El sender recibe un <see cref="IntegrationEventOutboxEntry"/> ya
/// hidratado de la BD (con su payload JSON, eventType, etc.) y es
/// responsable de despacharlo al topic <paramref name="topicName"/>.
/// Si lanza excepción, el worker incrementa <c>Attempts</c> y registra
/// el error sin marcar <c>PublishedAt</c>; la fila se reintenta en el
/// próximo poll mientras no exceda <c>MaxAttempts</c>.
/// </para>
///
/// <para>
/// <b>Contrato del topic (PR B):</b> el caller (típicamente
/// <c>OutboxPublisherWorker&lt;TDbContext&gt;</c>) determina a qué topic
/// publicar basado en sus options named por <c>nameof(TDbContext)</c>.
/// El sender es agnóstico al topic — recibe el nombre como parámetro y
/// despacha. Esto permite que múltiples workers (uno por módulo) reusen
/// un único sender singleton + un único <c>ServiceBusClient</c>
/// (compartido del namespace Service Bus del proyecto) sin acoplar el
/// sender a un topic fijo.
/// </para>
/// </summary>
public interface IIntegrationEventBusSender
{
    Task SendAsync(
        IntegrationEventOutboxEntry entry,
        string topicName,
        CancellationToken cancellationToken);
}
