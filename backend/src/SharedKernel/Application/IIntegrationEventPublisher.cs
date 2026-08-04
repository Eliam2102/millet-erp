namespace Millet.SharedKernel.Application;

/// <summary>
/// Puerto cross-módulo para publicar eventos de integración (eventos
/// que cruzan bounded contexts y eventualmente viajan a un broker
/// externo como Service Bus o se persisten en un Outbox transaccional).
///
/// <para>
/// Distinto de <c>MediatR.INotification</c>: aquellos eventos viven
/// in-proc dentro del mismo módulo y se publican vía <c>IMediator</c>.
/// Los eventos de integración salen del proceso (otro módulo, otro
/// servicio, otro consumer externo) y requieren entrega garantizada,
/// no best-effort.
/// </para>
/// <para>
/// El payload es <c>object</c> a propósito: cada módulo define sus
/// propios contratos serializables y el adapter de transporte se
/// encarga de la envoltura (envelope, content type, etc.).
/// </para>
/// </summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync(object integrationEvent, CancellationToken cancellationToken);
}
