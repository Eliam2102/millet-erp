namespace Millet.Integraciones.Aw.Application;

/// <summary>
/// Settings del webhook server-to-server que el ERP invoca en el Glass
/// Agent cuando adjunta el PDF de A+W, para que el Agent persista
/// <c>pdf_url</c> al instante sin depender de que la pestaña del vendedor
/// esté abierta (entrega "live" independiente del browser). Bindeadas
/// desde la sección <c>IntegracionesAw:AgentWebhook</c> + KV refs en
/// QA/Prod.
///
/// <para>
/// <b>Sin config = NoOp</b>: si <see cref="Url"/> o <see cref="Secret"/>
/// están vacíos, el DI registra <c>NoOpAgentDocumentoWebhook</c> y el ERP
/// no intenta llamar al Agent. Mismo patrón que <see cref="SoketiOptions"/>.
/// El cron del reconcile del Agent sigue siendo el respaldo garantizado.
/// </para>
/// </summary>
public sealed class AgentWebhookOptions
{
    public const string SectionName = "IntegracionesAw:AgentWebhook";

    /// <summary>
    /// URL absoluta del endpoint del Agent (ej.
    /// <c>https://cloud.tiglass.net/supportboard/glass-agent/erp_webhook.php</c>).
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Shared secret enviado en el header <c>X-Webhook-Secret</c>. KV ref en prod.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Timeout HTTP del POST al Agent (segundos). Default 5.</summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>True si hay lo mínimo para llamar (Url + Secret). El DI usa esto para Http vs NoOp.</summary>
    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(Url) &&
        !string.IsNullOrWhiteSpace(Secret);
}
