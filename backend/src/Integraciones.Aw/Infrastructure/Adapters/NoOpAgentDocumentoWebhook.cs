using Microsoft.Extensions.Logging;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.Infrastructure.Adapters;

/// <summary>
/// Implementación no-op de <see cref="IAgentDocumentoWebhook"/> para dev/local
/// o cuando <c>IntegracionesAw:AgentWebhook</c> no está configurado. El ERP no
/// llama al Agent; el cron del reconcile del Agent sigue siendo el respaldo.
/// </summary>
public sealed class NoOpAgentDocumentoWebhook : IAgentDocumentoWebhook
{
    private readonly ILogger<NoOpAgentDocumentoWebhook> _logger;

    public NoOpAgentDocumentoWebhook(ILogger<NoOpAgentDocumentoWebhook> logger)
    {
        _logger = logger;
    }

    public Task NotifyPdfListoAsync(EntidadExterna entidad, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Agent webhook NoOp (sin config). aggregate={Id} — el cron del Agent persistirá pdf_url.",
            entidad.Id);
        return Task.CompletedTask;
    }
}
