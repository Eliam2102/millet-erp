namespace Millet.Integraciones.Aw.Application;

/// <summary>
/// Settings del cliente Soketi/Pusher que el ERP usa para empujar cambios
/// de estado al canal realtime de Glass Agent. Bindeadas desde la sección
/// <c>Soketi</c> de <c>appsettings.json</c> + KV refs en QA/Prod.
///
/// <para>
/// <b>Sin config = NoOp</b>: si <see cref="Secret"/> o <see cref="AppId"/>
/// están vacíos, el DI registra <c>NoOpAgentRealtimePublisher</c> y el
/// ERP no intenta conectar a Soketi. Patrón equivalente al de
/// <c>ServiceBus.ConnectionString</c> vacío → <c>NoOpIntegrationEventBusSender</c>.
/// Útil para dev local sin Soketi.
/// </para>
/// </summary>
public sealed class SoketiOptions
{
    public const string SectionName = "Soketi";

    /// <summary>App ID configurado en el Soketi server. Ej. <c>tiglass-app</c>.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>App key (público — usado por la UI para suscribirse).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>App secret (firma de eventos publicados). KV ref en QA/Prod.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Host del Soketi server (sin esquema). Ej. <c>ws.tiglass.net</c>.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Puerto. 443 para TLS, 6001 para dev sin TLS.</summary>
    public int Port { get; set; } = 443;

    /// <summary>true para wss:// (prod), false para ws:// (dev local).</summary>
    public bool UseTls { get; set; } = true;

    /// <summary>
    /// Prefijo del canal Soketi al que se publica. Por defecto
    /// <c>private-user-</c> — combinado con <c>{operator_user_id}</c> de
    /// la cotización forma <c>private-user-67</c>, etc.
    /// </summary>
    public string ChannelPrefix { get; set; } = "private-user-";

    /// <summary>
    /// Nombre del evento Pusher emitido. Único para todas las
    /// transiciones de estado — la UI lee <c>estado</c> del payload para
    /// discriminar.
    /// </summary>
    public string EventName { get; set; } = "aw-cotizacion-actualizada";

    /// <summary>
    /// True si la config tiene lo mínimo para publicar (secret + appId).
    /// El DI usa esto para decidir entre Soketi y NoOp.
    /// </summary>
    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(AppId) &&
        !string.IsNullOrWhiteSpace(Secret) &&
        !string.IsNullOrWhiteSpace(Host);
}
