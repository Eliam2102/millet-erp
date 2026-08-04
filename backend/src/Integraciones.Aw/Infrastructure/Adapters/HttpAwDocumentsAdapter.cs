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
/// Implementación HTTP de <see cref="IAwDocumentsReader"/> contra los
/// endpoints <c>GET /documents</c> y <c>GET /documents/{filename}</c> del
/// drop service on-prem. El tráfico se rutea automáticamente vía Hybrid
/// Connection.
///
/// <para>
/// <b>Reuso del HttpClient named:</b> comparte
/// <c>HttpAwDropAdapter.HttpClientName</c> — es el mismo servicio on-prem
/// con el mismo BaseAddress y misma API key, igual que
/// <see cref="HttpAwCompletionsAdapter"/>.
/// </para>
/// </summary>
public sealed class HttpAwDocumentsAdapter : IAwDocumentsReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly IntegracionesAwOptions _options;
    private readonly ILogger<HttpAwDocumentsAdapter> _logger;

    public HttpAwDocumentsAdapter(
        IHttpClientFactory httpClientFactory,
        IOptions<IntegracionesAwOptions> options,
        ILogger<HttpAwDocumentsAdapter> logger)
    {
        // Reusa el mismo named client que el drop adapter — mismo servicio.
        _http = httpClientFactory.CreateClient(HttpAwDropAdapter.HttpClientName);
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AwDocumentsPage> ListSinceAsync(
        DateTimeOffset? since,
        int limit,
        CancellationToken cancellationToken)
    {
        using var activity = IntegracionesAwActivitySource.Instance.StartActivity(
            "HttpAwDocumentsAdapter.ListSince");
        activity?.SetTag("aw.documents.since", since?.ToString("O"));
        activity?.SetTag("aw.documents.limit", limit);

        var url = BuildListUrl(since, limit);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddApiKey(request);

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
                "GET /documents timeout. since={Since} limit={Limit} duration_ms={DurationMs}",
                since, limit, sw.ElapsedMilliseconds);
            throw new AwDocumentsException(
                $"Timeout HTTP al drop service ({sw.ElapsedMilliseconds}ms).",
                kind: "timeout", isTransient: true, inner: ex);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "GET /documents network error. since={Since} limit={Limit}", since, limit);
            throw new AwDocumentsException(
                $"Error de red al drop service: {ex.Message}",
                kind: "network", isTransient: true, inner: ex);
        }
        sw.Stop();

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw MapHttpError(response.StatusCode,
                    await SafeReadBodyAsync(response, cancellationToken), sw.ElapsedMilliseconds, "documents");
            }

            DocumentsResponseDto? parsed;
            try
            {
                var bodyText = await response.Content.ReadAsStringAsync(cancellationToken);
                parsed = JsonSerializer.Deserialize<DocumentsResponseDto>(bodyText, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "GET /documents response 200 con body no parseable. duration_ms={DurationMs}",
                    sw.ElapsedMilliseconds);
                throw new AwDocumentsException(
                    "Body JSON inválido del drop service.",
                    kind: "invalid_response", isTransient: true, inner: ex);
            }

            if (parsed is null)
            {
                throw new AwDocumentsException(
                    "Body vacío del drop service.",
                    kind: "invalid_response", isTransient: true);
            }

            var items = (parsed.Documents ?? Array.Empty<DocumentItemDto>())
                .Select(MapItem)
                .ToList();

            _logger.LogInformation(
                "GET /documents OK. since={Since} limit={Limit} returned={Count} duration_ms={DurationMs}",
                since, limit, items.Count, sw.ElapsedMilliseconds);

            return new AwDocumentsPage(
                Documents: items,
                NextSince: parsed.NextSince);
        }
    }

    public async Task<Stream> DownloadAsync(string filename, CancellationToken cancellationToken)
    {
        using var activity = IntegracionesAwActivitySource.Instance.StartActivity(
            "HttpAwDocumentsAdapter.Download");
        activity?.SetTag("aw.documents.filename", filename);

        var url = "/documents/" + Uri.EscapeDataString(filename);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddApiKey(request);

        HttpResponseMessage response;
        var sw = Stopwatch.StartNew();
        try
        {
            // ResponseHeadersRead: no bufferiza el body completo en memoria —
            // el stream se pasa directo a Blob Storage.
            response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            request.Dispose();
            throw new AwDocumentsException(
                $"Timeout HTTP al descargar documento ({sw.ElapsedMilliseconds}ms).",
                kind: "timeout", isTransient: true, inner: ex);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            request.Dispose();
            throw new AwDocumentsException(
                $"Error de red al descargar documento: {ex.Message}",
                kind: "network", isTransient: true, inner: ex);
        }
        sw.Stop();

        if (!response.IsSuccessStatusCode)
        {
            var body = await SafeReadBodyAsync(response, cancellationToken);
            response.Dispose();
            request.Dispose();
            throw MapHttpError(response.StatusCode, body, sw.ElapsedMilliseconds, "download");
        }

        _logger.LogInformation(
            "GET /documents/{Filename} OK. duration_ms={DurationMs}", filename, sw.ElapsedMilliseconds);

        // El stream cierra la response + request al disponerse (StreamWithCleanup).
        var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        return new StreamWithCleanup(content, response, request);
    }

    public async Task ArchivarAsync(string filename, CancellationToken cancellationToken)
    {
        using var activity = IntegracionesAwActivitySource.Instance.StartActivity(
            "HttpAwDocumentsAdapter.Archivar");
        activity?.SetTag("aw.documents.filename", filename);

        var url = "/documents/" + Uri.EscapeDataString(filename) + "/archive";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        AddApiKey(request);

        HttpResponseMessage response;
        var sw = Stopwatch.StartNew();
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AwDocumentsException(
                $"Timeout HTTP al archivar documento ({sw.ElapsedMilliseconds}ms).",
                kind: "timeout", isTransient: true, inner: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AwDocumentsException(
                $"Error de red al archivar documento: {ex.Message}",
                kind: "network", isTransient: true, inner: ex);
        }

        using (response)
        {
            // El drop service responde 200 tanto si movió como si ya no estaba
            // (idempotente). Solo errores reales (5xx/4xx) se propagan.
            if (!response.IsSuccessStatusCode)
            {
                throw MapHttpError(response.StatusCode,
                    await SafeReadBodyAsync(response, cancellationToken), sw.ElapsedMilliseconds, "archive");
            }
            _logger.LogInformation(
                "POST /documents/{Filename}/archive OK. duration_ms={DurationMs}",
                filename, sw.ElapsedMilliseconds);
        }
    }

    private void AddApiKey(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_options.DropServiceApiKey)) return;

        var apiKey = _options.DropServiceApiKey.Trim();
        try
        {
            request.Headers.Add("X-API-Key", apiKey);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            _logger.LogError(ex,
                "API key del drop service tiene caracteres inválidos para HTTP header.");
            throw new AwDocumentsException(
                "API key del drop service tiene caracteres inválidos para HTTP header.",
                kind: "invalid_api_key", isTransient: false, inner: ex);
        }
    }

    private AwDocumentsException MapHttpError(
        HttpStatusCode status, string body, long durationMs, string op)
    {
        var isTransient = (int)status >= 500
            || status == HttpStatusCode.RequestTimeout
            || status == HttpStatusCode.TooManyRequests;

        var kind = status switch
        {
            HttpStatusCode.Unauthorized => "http_401",
            HttpStatusCode.Forbidden => "http_403",
            HttpStatusCode.BadRequest => "http_400",
            HttpStatusCode.NotFound => "not_found",
            HttpStatusCode.RequestTimeout => "http_408",
            HttpStatusCode.TooManyRequests => "http_429",
            _ when (int)status >= 500 => "http_5xx",
            _ when (int)status >= 400 => "http_4xx",
            _ => "http_other",
        };

        _logger.LogWarning(
            "GET /{Op} fallo. status={Status} kind={Kind} transient={Transient} duration_ms={DurationMs}",
            op, (int)status, kind, isTransient, durationMs);

        return new AwDocumentsException(
            $"Drop service respondió {(int)status} {status}: {Truncate(body, 256)}",
            kind, isTransient);
    }

    private static async Task<string> SafeReadBodyAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadAsStringAsync(ct); }
        catch { return string.Empty; }
    }

    private static AwDocumentItem MapItem(DocumentItemDto dto) =>
        new(
            Filename: dto.Filename ?? string.Empty,
            DocType: (dto.DocType ?? string.Empty).ToLowerInvariant(),
            AwDocId: dto.AwDocId ?? 0L,
            SizeBytes: dto.SizeBytes ?? 0L,
            ModifiedAt: dto.ModifiedAt ?? DateTimeOffset.MinValue);

    private static string BuildListUrl(DateTimeOffset? since, int limit)
    {
        var qs = new List<string> { $"limit={limit}" };
        if (since is { } s)
        {
            qs.Add($"since={Uri.EscapeDataString(s.ToString("O", CultureInfo.InvariantCulture))}");
        }
        return "/documents?" + string.Join("&", qs);
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...";

    /// <summary>DTOs internos para deserializar el JSON snake_case.</summary>
    private sealed record DocumentsResponseDto(
        IReadOnlyList<DocumentItemDto>? Documents,
        DateTimeOffset? NextSince);

    private sealed record DocumentItemDto(
        string? Filename,
        string? DocType,
        long? AwDocId,
        long? SizeBytes,
        DateTimeOffset? ModifiedAt);

    /// <summary>
    /// Stream que, al disponerse, también dispone la <see cref="HttpResponseMessage"/>
    /// y la <see cref="HttpRequestMessage"/> subyacentes — evita fugas de
    /// conexión cuando el caller solo dispone el stream.
    /// </summary>
    private sealed class StreamWithCleanup : Stream
    {
        private readonly Stream _inner;
        private readonly IDisposable _response;
        private readonly IDisposable _request;

        public StreamWithCleanup(Stream inner, IDisposable response, IDisposable request)
        {
            _inner = inner;
            _response = response;
            _request = request;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            _inner.ReadAsync(buffer, offset, count, ct);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            _inner.ReadAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
                _request.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            _response.Dispose();
            _request.Dispose();
            await base.DisposeAsync();
        }
    }
}
