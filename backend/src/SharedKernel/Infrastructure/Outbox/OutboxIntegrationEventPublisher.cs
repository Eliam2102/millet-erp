using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.SharedKernel.Infrastructure.Outbox;

/// <summary>
/// Implementación real (F6-PR1) de <see cref="IIntegrationEventPublisher"/>.
/// No publica al broker directamente — encola al
/// <see cref="IIntegrationEventBuffer"/> scoped, y el
/// <c>OutboxSaveChangesInterceptor</c> drena el buffer dentro de la TX EF
/// para insertar las filas en <c>integration_events_outbox</c>.
///
/// <para>
/// <b>Fail-loud por contrato</b>: el caller debe pasar un
/// <see cref="IntegrationEvent"/> concreto. <c>object</c>'s no
/// derivados de la base se rechazan con <see cref="ArgumentException"/>.
/// Esto evita pérdidas silenciosas — los integration events son
/// críticos (entrega garantizada).
/// </para>
/// </summary>
public sealed class OutboxIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly IIntegrationEventBuffer _buffer;
    private readonly ILogger<OutboxIntegrationEventPublisher> _logger;

    public OutboxIntegrationEventPublisher(
        IIntegrationEventBuffer buffer,
        ILogger<OutboxIntegrationEventPublisher> logger)
    {
        _buffer = buffer;
        _logger = logger;
    }

    public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent is not IntegrationEvent typed)
        {
            throw new ArgumentException(
                $"El evento debe derivar de {nameof(IntegrationEvent)} (recibido: {integrationEvent.GetType().FullName}).",
                nameof(integrationEvent));
        }

        _buffer.Enqueue(typed);
        _logger.LogDebug(
            "Integration event encolado al outbox buffer. EventType={EventType} EmpresaId={EmpresaId}",
            typed.EventType,
            typed.EmpresaId);
        return Task.CompletedTask;
    }
}
