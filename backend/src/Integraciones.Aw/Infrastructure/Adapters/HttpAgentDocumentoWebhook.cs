using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.Infrastructure.Adapters;

/// <summary>
/// Implementación productiva de <see cref="IAgentDocumentoWebhook"/>: POST
/// server-to-server al endpoint del Glass Agent
/// (<c>AgentWebhookOptions.Url</c>) con el shared secret en el header
/// <c>X-Webhook-Secret</c>. El App Service del ERP tiene salida a internet,
/// así que alcanza el host público del Agent (no aplica el inbound-only del
/// drop service).
///
/// <para>
/// <b>Best-effort</b>: cualquier excepción (red, timeout, 4xx/5xx del Agent)
/// se loguea como warning y se traga. El PDF ya está en el ERP y el cron del
/// Agent es el respaldo.
/// </para>
/// </summary>
public sealed class HttpAgentDocumentoWebhook : IAgentDocumentoWebhook
{
    /// <summary>Nombre del named HttpClient configurado en DI.</summary>
    public const string HttpClientName = "AgentWebhook";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly HttpClient _http;
    private readonly AgentWebhookOptions _options;
    private readonly ILogger<HttpAgentDocumentoWebhook> _logger;

    public HttpAgentDocumentoWebhook(
        IHttpClientFactory httpClientFactory,
        IOptions<AgentWebhookOptions> options,
        ILogger<HttpAgentDocumentoWebhook> logger)
    {
        _http = httpClientFactory.CreateClient(HttpClientName);
        _options = options.Value;
        _logger = logger;
    }

    public async Task NotifyPdfListoAsync(EntidadExterna entidad, CancellationToken cancellationToken)
    {
        using var activity = IntegracionesAwActivitySource.Instance.StartActivity(
            "HttpAgentDocumentoWebhook.NotifyPdfListo");
        activity?.SetTag("aw.webhook.aggregate", entidad.Id);

        // pdf_url apunta al endpoint proxy autenticado del ERP (no al blob interno).
        var pdfUrl = entidad.PdfBlobUrl is null
            ? null
            : $"/api/v1/integraciones/aw/cotizaciones/{entidad.Id}/pdf";

        var payload = new WebhookPayload(
            ErpId: entidad.Id,
            QuoteReference: entidad.ReferenciaExterna,
            Estado: (int)entidad.Estado,
            AwDocId: entidad.AwDocId,
            // correlated_at autoritativo (estable): el Agent lo usa para NO
            // sobreescribir aw_correlated_at con la hora del PDF.
            CorrelatedAt: entidad.CorrelatedAt?.ToString("o"),
            PdfUrl: pdfUrl,
            PdfFilename: entidad.PdfFilename,
            PdfUploadedAt: entidad.PdfUploadedAt?.ToString("o"));

        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.Url)
            {
                Content = JsonContent.Create(payload, options: JsonOptions),
            };
            request.Headers.Add("X-Webhook-Secret", _options.Secret.Trim());

            using var response = await _http.SendAsync(request, cancellationToken);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Agent webhook OK. aggregate={Id} status={Status} duration_ms={DurationMs}",
                    entidad.Id, (int)response.StatusCode, sw.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogWarning(
                    "Agent webhook fallo (best-effort; el cron del Agent cubre). aggregate={Id} status={Status} duration_ms={DurationMs}",
                    entidad.Id, (int)response.StatusCode, sw.ElapsedMilliseconds);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "Agent webhook error (best-effort; el cron del Agent cubre). aggregate={Id} duration_ms={DurationMs}",
                entidad.Id, sw.ElapsedMilliseconds);
        }
    }

    /// <summary>Payload snake_case que consume <c>erp_webhook.php</c> del Agent.</summary>
    private sealed record WebhookPayload(
        Guid ErpId,
        string QuoteReference,
        int Estado,
        long? AwDocId,
        string? CorrelatedAt,
        string? PdfUrl,
        string? PdfFilename,
        string? PdfUploadedAt);
}
