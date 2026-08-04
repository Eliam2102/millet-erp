using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application;
using Millet.Integraciones.Aw.Application.Ports;

namespace Millet.Integraciones.Aw.Infrastructure.Adapters;

/// <summary>
/// Implementación HTTP de <see cref="IAwCompletionsReader"/> contra el
/// endpoint <c>GET /completions</c> del drop service on-prem. El tráfico
/// se rutea automáticamente vía Hybrid Connection.
///
/// <para>
/// <b>Reuso del HttpClient named:</b> comparte
/// <c>HttpAwDropAdapter.HttpClientName</c> — es el mismo servicio on-prem
/// con el mismo BaseAddress y misma API key. El timeout del HttpClient
/// (configurado en DI) es generoso para drops (~180s); para
/// <c>/completions</c> el response es típicamente sub-segundo.
/// </para>
///
/// <para>
/// <b>Errores tipados:</b> mapea status HTTP + excepciones de red a
/// <see cref="AwCompletionsException"/> con <c>IsTransient</c> y
/// <c>Kind</c> consistentes con el adapter de drop.
/// </para>
/// </summary>
public sealed class HttpAwCompletionsAdapter : IAwCompletionsReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly IntegracionesAwOptions _options;
    private readonly ILogger<HttpAwCompletionsAdapter> _logger;

    public HttpAwCompletionsAdapter(
        IHttpClientFactory httpClientFactory,
        IOptions<IntegracionesAwOptions> options,
        ILogger<HttpAwCompletionsAdapter> logger)
    {
        // Reusa el mismo named client que el drop adapter — mismo servicio.
        _http = httpClientFactory.CreateClient(HttpAwDropAdapter.HttpClientName);
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AwCompletionsPage> ListSinceAsync(
        DateTimeOffset? since,
        int limit,
        CancellationToken cancellationToken)
    {
        using var activity = IntegracionesAwActivitySource.Instance.StartActivity(
            "HttpAwCompletionsAdapter.ListSince");
        activity?.SetTag("aw.completions.since", since?.ToString("O"));
        activity?.SetTag("aw.completions.limit", limit);

        var url = BuildUrl(since, limit);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        // Trim defensivo del API key (mismo razonamiento que HttpAwDropAdapter:
        // secrets en KV pueden traer CR/LF trailing y romper Headers.Add).
        if (!string.IsNullOrWhiteSpace(_options.DropServiceApiKey))
        {
            var apiKey = _options.DropServiceApiKey.Trim();
            try
            {
                request.Headers.Add("X-API-Key", apiKey);
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
                _logger.LogError(ex,
                    "API key del drop service tiene caracteres inválidos para HTTP header.");
                throw new AwCompletionsException(
                    "API key del drop service tiene caracteres inválidos para HTTP header.",
                    kind: "invalid_api_key", isTransient: false, inner: ex);
            }
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
                "GET /completions timeout. since={Since} limit={Limit} duration_ms={DurationMs}",
                since, limit, sw.ElapsedMilliseconds);
            throw new AwCompletionsException(
                $"Timeout HTTP al drop service ({sw.ElapsedMilliseconds}ms).",
                kind: "timeout", isTransient: true, inner: ex);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "GET /completions network error. since={Since} limit={Limit}", since, limit);
            throw new AwCompletionsException(
                $"Error de red al drop service: {ex.Message}",
                kind: "network", isTransient: true, inner: ex);
        }
        sw.Stop();

        using (response)
        {
            var status = response.StatusCode;
            activity?.SetTag("http.response.status_code", (int)status);

            if (!response.IsSuccessStatusCode)
            {
                var isTransient = (int)status >= 500
                    || status == HttpStatusCode.RequestTimeout
                    || status == HttpStatusCode.TooManyRequests;

                var kind = status switch
                {
                    HttpStatusCode.Unauthorized => "http_401",
                    HttpStatusCode.Forbidden => "http_403",
                    HttpStatusCode.BadRequest => "http_400",
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
                catch { /* opcional para el error */ }

                _logger.LogWarning(
                    "GET /completions fallo. status={Status} kind={Kind} transient={Transient} duration_ms={DurationMs}",
                    (int)status, kind, isTransient, sw.ElapsedMilliseconds);

                throw new AwCompletionsException(
                    $"Drop service respondió {(int)status} {status}: {Truncate(body, 256)}",
                    kind, isTransient);
            }

            // 2xx — parse del JSON. Si el body no parsea, lo tratamos como
            // error transient (drop service viejo o response truncada).
            CompletionsResponseDto? parsed;
            try
            {
                var bodyText = await response.Content.ReadAsStringAsync(cancellationToken);
                parsed = JsonSerializer.Deserialize<CompletionsResponseDto>(bodyText, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "GET /completions response 200 con body no parseable. duration_ms={DurationMs}",
                    sw.ElapsedMilliseconds);
                throw new AwCompletionsException(
                    "Body JSON inválido del drop service.",
                    kind: "invalid_response", isTransient: true, inner: ex);
            }

            if (parsed is null)
            {
                throw new AwCompletionsException(
                    "Body vacío del drop service.",
                    kind: "invalid_response", isTransient: true);
            }

            var items = (parsed.Completions ?? Array.Empty<CompletionItemDto>())
                .Select(MapItem)
                .ToList();

            _logger.LogInformation(
                "GET /completions OK. since={Since} limit={Limit} returned={Count} duration_ms={DurationMs}",
                since, limit, items.Count, sw.ElapsedMilliseconds);

            return new AwCompletionsPage(
                Completions: items,
                NextSince: parsed.NextSince);
        }
    }

    private static AwCompletionItem MapItem(CompletionItemDto dto)
    {
        var outcome = dto.Outcome?.ToLowerInvariant() switch
        {
            "success" => DropOutcome.Success,
            "failed" => DropOutcome.Failed,
            "stuck" => DropOutcome.Stuck,
            _ => DropOutcome.Unknown,
        };

        return new AwCompletionItem(
            Filename: dto.Filename ?? string.Empty,
            Outcome: outcome,
            AwDocId: dto.AwDocId,
            ErrorCodes: dto.AwErrorCodes ?? Array.Empty<string>(),
            ErrorMessage: dto.AwErrorMessage,
            DiagnosticLog: dto.AwDiagnosticLog,
            ParsedAt: dto.ParsedAt ?? DateTimeOffset.MinValue,
            Lane: dto.Lane ?? string.Empty);
    }

    private static string BuildUrl(DateTimeOffset? since, int limit)
    {
        var qs = new List<string> { $"limit={limit}" };
        if (since is { } s)
        {
            // ISO 8601 con offset, formato "O" — interpretable por el endpoint
            // (DateTimeOffset.TryParse con AssumeUniversal+AdjustToUniversal).
            qs.Add($"since={Uri.EscapeDataString(s.ToString("O", CultureInfo.InvariantCulture))}");
        }
        return "/completions?" + string.Join("&", qs);
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...";

    /// <summary>
    /// DTOs internos para deserializar el JSON snake_case del drop service.
    /// </summary>
    private sealed record CompletionsResponseDto(
        IReadOnlyList<CompletionItemDto>? Completions,
        DateTimeOffset? NextSince);

    private sealed record CompletionItemDto(
        string? Filename,
        string? Outcome,
        long? AwDocId,
        IReadOnlyList<string>? AwErrorCodes,
        string? AwErrorMessage,
        string? AwDiagnosticLog,
        DateTimeOffset? ParsedAt,
        string? Lane);
}
