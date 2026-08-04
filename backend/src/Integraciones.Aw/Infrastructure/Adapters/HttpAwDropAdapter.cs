using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application;
using Millet.Integraciones.Aw.Application.Ports;

namespace Millet.Integraciones.Aw.Infrastructure.Adapters;

// AwDropException vive en Application.Ports (parte del contrato del puerto).

/// <summary>
/// Implementación HTTP de <see cref="IAwDropAdapter"/> contra el drop
/// service on-prem (Windows Service .NET 8 instalado en SER-DATA). El
/// tráfico se rutea automáticamente vía Hybrid Connection — el código
/// solo hace POST a <c>http://SER-DATA:5000/drop-edi</c> y Azure App
/// Service lo tunelea por la HC.
///
/// <para>
/// <b>Flow per-EDI (PR #199 + #201):</b> el drop service ahora bloquea
/// la respuesta HTTP hasta que A+W terminó de procesar el EDI (success,
/// failed o stuck). El response JSON trae el outcome final + datos
/// parseados del log de A+W (awDocId, códigos de error). Este adapter
/// deserializa esos campos y los pone en <see cref="DropResult"/>.
/// </para>
///
/// <para>
/// <b>Sin Polly retry:</b> el reintento lo maneja Service Bus
/// (Abandon → redelivery con backoff). Doble retry (Polly + SB) genera
/// amplificación exponencial y dificulta el reasoning sobre el tiempo
/// total entre Submitted y FailedDrop terminal.
/// </para>
/// </summary>
public sealed class HttpAwDropAdapter : IAwDropAdapter
{
    /// <summary>Named client de <c>IHttpClientFactory</c>.</summary>
    public const string HttpClientName = "AwDropService";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly IntegracionesAwOptions _options;
    private readonly ILogger<HttpAwDropAdapter> _logger;

    public HttpAwDropAdapter(
        IHttpClientFactory httpClientFactory,
        IOptions<IntegracionesAwOptions> options,
        ILogger<HttpAwDropAdapter> logger)
    {
        _http = httpClientFactory.CreateClient(HttpClientName);
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DropResult> SendEdiAsync(
        string filename,
        string ediContent,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        ArgumentException.ThrowIfNullOrWhiteSpace(ediContent);

        using var activity = IntegracionesAwActivitySource.Instance.StartActivity(
            "HttpAwDropAdapter.SendEdi");
        activity?.SetTag("aw.drop.filename", filename);
        activity?.SetTag("aw.drop.bytes_count", ediContent.Length);

        var bodyBytes = Encoding.UTF8.GetBytes(ediContent);
        using var content = new ByteArrayContent(bodyBytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/drop-edi")
        {
            Content = content,
        };

        // Trim defensivo. Bug detectado en validación E2E PR D: el secret
        // en KV se cargó con trailing "\r\r\n" (típico de operadores que
        // pegan vía portal o `Set-Content` sin -NoNewline). HttpClient
        // rechaza headers con whitespace de control y lanza
        // InvalidOperationException ANTES del SendAsync — la SendEdi
        // activity terminaba con duration < 1ms y sin HTTP real, exception
        // silenciosa en el catch general del worker, mensajes a DLQ.
        // Con Trim() la app es tolerante al ruido en el secret. La verdad
        // operativa sigue siendo "el secret en KV debe estar limpio" —
        // ver runbook en docs/integration/.
        if (!string.IsNullOrWhiteSpace(_options.DropServiceApiKey))
        {
            var apiKey = _options.DropServiceApiKey.Trim();
            try
            {
                request.Headers.Add("X-API-Key", apiKey);
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
                // Aún con Trim, si el secret contiene chars de control en
                // medio (no solo trailing), Headers.Add lanza. Convertir a
                // AwDropException permanente para que el worker la maneje
                // explícitamente (DLQ + métrica drop_failed is_terminal=true)
                // en lugar de caer al catch general silencioso.
                _logger.LogError(ex,
                    "API key del drop service tiene caracteres inválidos para HTTP header. " +
                    "Verifica el secret KV aw-drop-service-api-key (debe ser ASCII printable sin CR/LF/control).");
                throw new AwDropException(
                    "API key del drop service tiene caracteres inválidos para HTTP header.",
                    kind: "invalid_api_key", isTransient: false, inner: ex);
            }
        }

        try
        {
            request.Headers.Add("X-Filename", filename);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            // Filename normalmente lo construye el ERP (cot_{quoteRef}.edi)
            // y debe ser ASCII. Si llega con caracteres inválidos es bug de
            // upstream — permanente para evitar redelivery loop.
            throw new AwDropException(
                $"Filename '{filename}' tiene caracteres inválidos para HTTP header.",
                kind: "invalid_filename", isTransient: false, inner: ex);
        }

        HttpResponseMessage response;
        var sw = Stopwatch.StartNew();
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning(
                "Drop EDI timeout. filename={Filename} bytes={Bytes} duration_ms={DurationMs}",
                filename, bodyBytes.Length, sw.ElapsedMilliseconds);
            throw new AwDropException(
                $"Timeout HTTP al drop service ({sw.ElapsedMilliseconds}ms).",
                kind: "timeout", isTransient: true, inner: ex);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "Drop EDI network error. filename={Filename} bytes={Bytes}",
                filename, bodyBytes.Length);
            throw new AwDropException(
                $"Error de red al drop service: {ex.Message}",
                kind: "network", isTransient: true, inner: ex);
        }
        sw.Stop();

        using (response)
        {
            var status = response.StatusCode;
            activity?.SetTag("http.response.status_code", (int)status);

            if (response.IsSuccessStatusCode)
            {
                // Deserializa el JSON extendido del drop service (PR #199).
                // Si el body no parsea (drop service viejo o body vacío),
                // caemos a un DropResult mínimo con Outcome=Unknown — el
                // worker decide entonces el path legacy.
                DropServiceResponse? parsed = null;
                try
                {
                    var bodyText = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (!string.IsNullOrWhiteSpace(bodyText))
                    {
                        parsed = JsonSerializer.Deserialize<DropServiceResponse>(bodyText, JsonOptions);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Drop EDI response 200 con body no parseable. filename={Filename}. " +
                        "Tratando como outcome=unknown.",
                        filename);
                }

                var outcome = parsed?.Outcome?.ToLowerInvariant() switch
                {
                    "success" => DropOutcome.Success,
                    "failed" => DropOutcome.Failed,
                    "stuck" => DropOutcome.Stuck,
                    _ => DropOutcome.Unknown,
                };

                _logger.LogInformation(
                    "Drop EDI OK. filename={Filename} bytes={Bytes} duration_ms={DurationMs} " +
                    "outcome={Outcome} aw_doc_id={AwDocId} lane={Lane} waited_ms={WaitedMs}",
                    filename, bodyBytes.Length, sw.ElapsedMilliseconds,
                    outcome, parsed?.AwDocId, parsed?.Lane, parsed?.WaitedMs);

                activity?.SetTag("aw.drop.outcome", outcome.ToString());
                if (parsed?.AwDocId is { } docId) activity?.SetTag("aw.doc_id", docId);

                return new DropResult(
                    BytesWritten: parsed?.BytesWritten ?? bodyBytes.Length,
                    Path: parsed?.Path ?? string.Empty,
                    WrittenAt: parsed?.WrittenAt ?? DateTimeOffset.UtcNow,
                    Outcome: outcome,
                    AwDocId: parsed?.AwDocId,
                    AwErrorCodes: parsed?.AwErrorCodes,
                    AwErrorMessage: parsed?.AwErrorMessage,
                    AwDiagnosticLog: parsed?.AwDiagnosticLog,
                    Lane: parsed?.Lane,
                    WaitedMs: parsed?.WaitedMs);
            }

            // Clasificación 4xx/5xx → transient o permanent.
            var isTransient = (int)status >= 500
                || status == HttpStatusCode.RequestTimeout
                || status == HttpStatusCode.TooManyRequests;

            var kind = status switch
            {
                HttpStatusCode.Unauthorized => "http_401",
                HttpStatusCode.Forbidden => "http_403",
                HttpStatusCode.UnprocessableEntity => "http_422",
                HttpStatusCode.RequestTimeout => "http_408",
                HttpStatusCode.TooManyRequests => "http_429",
                _ when (int)status >= 500 => "http_5xx",
                _ when (int)status >= 400 => "http_4xx",
                _ => "http_other",
            };

            string body = string.Empty;
            try
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch { /* ignore — body opcional para el error */ }

            var message = $"Drop service respondió {(int)status} {status}: {Truncate(body, 256)}";
            _logger.LogWarning(
                "Drop EDI fallo. filename={Filename} bytes={Bytes} status={Status} kind={Kind} transient={Transient} duration_ms={DurationMs}",
                filename, bodyBytes.Length, (int)status, kind, isTransient, sw.ElapsedMilliseconds);

            throw new AwDropException(message, kind, isTransient);
        }
    }

    public async Task ArchiveResultAsync(string markerName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(markerName)) return;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/results/{Uri.EscapeDataString(markerName)}/archive");

        if (!string.IsNullOrWhiteSpace(_options.DropServiceApiKey))
        {
            try { request.Headers.Add("X-API-Key", _options.DropServiceApiKey.Trim()); }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
                _logger.LogWarning(ex,
                    "Archive result: API key inválida para header — se omite el ack de {Marker}.", markerName);
                return;
            }
        }

        // Best-effort: cualquier fallo se loggea pero NO se propaga. El marcador
        // queda en Results\ y /completions lo re-archivará (idempotente).
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Archive result no-2xx. marker={Marker} status={Status}", markerName, (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Archive result falló (best-effort). marker={Marker}", markerName);
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...";

    /// <summary>
    /// DTO interno para deserializar la respuesta JSON del drop service
    /// (campos en snake_case del lado on-prem). Property names en PascalCase
    /// + <see cref="JsonNamingPolicy.SnakeCaseLower"/> en las options
    /// resuelven el matching.
    /// </summary>
    private sealed record DropServiceResponse(
        string? Filename,
        string? Path,
        int BytesWritten,
        DateTimeOffset? WrittenAt,
        string? Outcome,
        long? AwDocId,
        IReadOnlyList<string>? AwErrorCodes,
        string? AwErrorMessage,
        string? AwDiagnosticLog,
        string? Lane,
        int? WaitedMs);
}
