using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application.Integration;

namespace Millet.SharedKernel.Infrastructure.Outbox;

/// <summary>
/// Fallback de <see cref="IIntegrationEventBusSender"/> cuando no hay
/// connection string de Service Bus configurada (F6-PR2). Útil para
/// dev local sin emulador: el worker corre normalmente, marca
/// <c>PublishedAt</c>, pero el evento no sale del proceso.
///
/// <para>
/// <b>NO usar en QA/Prod.</b> El wiring en <c>Program.cs</c> selecciona
/// el sender real (<see cref="ServiceBusIntegrationEventBusSender"/>)
/// cuando <c>Outbox:ServiceBusConnectionString</c> está poblada.
/// </para>
/// </summary>
public sealed class NoOpIntegrationEventBusSender : IIntegrationEventBusSender
{
    private readonly ILogger<NoOpIntegrationEventBusSender> _logger;

    public NoOpIntegrationEventBusSender(ILogger<NoOpIntegrationEventBusSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(
        IntegrationEventOutboxEntry entry,
        string topicName,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "[NoOpIntegrationEventBusSender] descartado evento {EventType} (sin Service Bus configurado). Topic={Topic} Id={Id}",
            entry.EventType,
            topicName,
            entry.Id);
        return Task.CompletedTask;
    }
}
