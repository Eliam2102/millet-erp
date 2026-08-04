using Microsoft.Extensions.Logging;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.Infrastructure.Adapters;

/// <summary>
/// Implementación NoOp de <see cref="IAgentRealtimePublisher"/>. Se
/// registra cuando <c>SoketiOptions.IsEnabled</c> es false (dev local
/// sin Soketi, o QA/Prod con KV refs vacías). Logueando cada llamada en
/// Debug para diagnóstico, sin tirar ni intentar conectar a ningún
/// servidor.
///
/// // PLATFORM-TODO(&lt;AgentRealtimePublisher&gt;): cuando el deploy a
/// SER-DATA tenga el Soketi server configurado y los secrets cargados
/// en KV, el switch del DI selecciona <c>SoketiAgentRealtimePublisher</c>
/// automáticamente y esta clase deja de usarse.
/// </summary>
public sealed class NoOpAgentRealtimePublisher : IAgentRealtimePublisher
{
    private readonly ILogger<NoOpAgentRealtimePublisher> _logger;

    public NoOpAgentRealtimePublisher(ILogger<NoOpAgentRealtimePublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishCotizacionActualizadaAsync(
        EntidadExterna entidad,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "AgentRealtimePublisher NoOp: skip push para aggregate={Id} quoteRef={QuoteRef} estado={Estado}.",
            entidad.Id, entidad.ReferenciaExterna, entidad.Estado);
        return Task.CompletedTask;
    }
}
