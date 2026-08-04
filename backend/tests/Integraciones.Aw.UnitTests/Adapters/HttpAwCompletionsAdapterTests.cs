using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Infrastructure.Adapters;

namespace Millet.Integraciones.Aw.UnitTests.Adapters;

/// <summary>
/// Tests del <see cref="HttpAwCompletionsAdapter"/> (PR-3 del feature
/// late-reconciliation). Cubren parsing del JSON, mapeo de outcomes,
/// construcción de query string, clasificación de errores HTTP y manejo
/// de API key.
/// </summary>
public sealed class HttpAwCompletionsAdapterTests
{
    private static readonly string[] CodeFailedSample = ["1555"];

    private const string SampleSuccessBody = """
        {
          "completions": [
            {
              "filename": "cot_NIN_Q-2026-00346.edi",
              "outcome": "success",
              "aw_doc_id": 31247,
              "aw_error_codes": [],
              "aw_error_message": null,
              "aw_diagnostic_log": "(4615) [Documento]=31247",
              "parsed_at": "2026-05-29T10:33:12.118+00:00",
              "lane": "default"
            }
          ],
          "next_since": "2026-05-29T10:33:12.118+00:00"
        }
        """;

    [Fact]
    public async Task ListSince_ResponseOk_ParseaCompletionsYNextSince()
    {
        var adapter = BuildAdapter(_ => CreateResponse(HttpStatusCode.OK, SampleSuccessBody));

        var page = await adapter.ListSinceAsync(since: null, limit: 500, CancellationToken.None);

        page.Completions.Should().HaveCount(1);
        var item = page.Completions[0];
        item.Filename.Should().Be("cot_NIN_Q-2026-00346.edi");
        item.Outcome.Should().Be(DropOutcome.Success);
        item.AwDocId.Should().Be(31247L);
        item.ErrorCodes.Should().BeEmpty();
        item.DiagnosticLog.Should().Contain("31247");
        item.Lane.Should().Be("default");

        page.NextSince.Should().Be(item.ParsedAt);
    }

    [Fact]
    public async Task ListSince_OutcomeMapeo_FailedYStuckYUnknown()
    {
        const string body = """
            {
              "completions": [
                {"filename":"a.edi","outcome":"failed","aw_doc_id":null,
                 "aw_error_codes":["1555"],"aw_error_message":"rechazado",
                 "parsed_at":"2026-05-29T10:00:00Z","lane":"default"},
                {"filename":"b.edi","outcome":"stuck","aw_doc_id":null,
                 "parsed_at":"2026-05-29T10:00:01Z","lane":"default"},
                {"filename":"c.edi","outcome":"garbage","aw_doc_id":null,
                 "parsed_at":"2026-05-29T10:00:02Z","lane":"default"}
              ],
              "next_since":"2026-05-29T10:00:02Z"
            }
            """;
        var adapter = BuildAdapter(_ => CreateResponse(HttpStatusCode.OK, body));

        var page = await adapter.ListSinceAsync(null, 500, CancellationToken.None);

        page.Completions[0].Outcome.Should().Be(DropOutcome.Failed);
        page.Completions[0].ErrorCodes.Should().BeEquivalentTo(CodeFailedSample);
        page.Completions[1].Outcome.Should().Be(DropOutcome.Stuck);
        page.Completions[2].Outcome.Should().Be(DropOutcome.Unknown);
    }

    [Fact]
    public async Task ListSince_ResponseVacia_RetornaPaginaVaciaYNextSinceNull()
    {
        const string body = """{"completions":[],"next_since":null}""";
        var adapter = BuildAdapter(_ => CreateResponse(HttpStatusCode.OK, body));

        var page = await adapter.ListSinceAsync(null, 500, CancellationToken.None);

        page.Completions.Should().BeEmpty();
        page.NextSince.Should().BeNull();
    }

    [Fact]
    public async Task ListSince_ConstruyeQueryString_ConSinceYLimit()
    {
        string? capturedUrl = null;
        var adapter = BuildAdapter(req =>
        {
            capturedUrl = req.RequestUri?.PathAndQuery;
            return CreateResponse(HttpStatusCode.OK, """{"completions":[],"next_since":null}""");
        });
        var since = new DateTimeOffset(2026, 5, 29, 10, 0, 0, TimeSpan.Zero);

        await adapter.ListSinceAsync(since, limit: 250, CancellationToken.None);

        capturedUrl.Should().StartWith("/completions?");
        capturedUrl.Should().Contain("limit=250");
        capturedUrl.Should().Contain("since=");
        // since va URL-encoded; el server lo deserializa con DateTimeOffset.TryParse.
        Uri.UnescapeDataString(capturedUrl!).Should().Contain("2026-05-29T10:00:00");
    }

    [Fact]
    public async Task ListSince_SinSince_NoIncluyeQueryParam()
    {
        string? capturedUrl = null;
        var adapter = BuildAdapter(req =>
        {
            capturedUrl = req.RequestUri?.PathAndQuery;
            return CreateResponse(HttpStatusCode.OK, """{"completions":[],"next_since":null}""");
        });

        await adapter.ListSinceAsync(since: null, limit: 500, CancellationToken.None);

        capturedUrl.Should().Be("/completions?limit=500");
    }

    [Fact]
    public async Task ListSince_ConApiKey_EnviaHeaderXApiKey()
    {
        string? capturedApiKey = null;
        var adapter = BuildAdapter(
            handler: req =>
            {
                capturedApiKey = req.Headers.GetValues("X-API-Key").FirstOrDefault();
                return CreateResponse(HttpStatusCode.OK, """{"completions":[],"next_since":null}""");
            },
            apiKey: "secret-key-123");

        await adapter.ListSinceAsync(null, 500, CancellationToken.None);

        capturedApiKey.Should().Be("secret-key-123");
    }

    [Fact]
    public async Task ListSince_ApiKeyConTrailingNewlines_LaTrimea()
    {
        // Mismo bug detectado en HttpAwDropAdapter: secret KV con "\r\r\n"
        // trailing rompe Headers.Add sin Trim defensivo.
        string? capturedApiKey = null;
        var adapter = BuildAdapter(
            handler: req =>
            {
                capturedApiKey = req.Headers.GetValues("X-API-Key").FirstOrDefault();
                return CreateResponse(HttpStatusCode.OK, """{"completions":[],"next_since":null}""");
            },
            apiKey: "real-secret\r\r\n");

        await adapter.ListSinceAsync(null, 500, CancellationToken.None);

        capturedApiKey.Should().Be("real-secret");
    }

    [Fact]
    public async Task ListSince_ApiKeyConCharsInvalidosEnMedio_LanzaPermanent()
    {
        var adapter = BuildAdapter(
            handler: _ => CreateResponse(HttpStatusCode.OK, ""),
            apiKey: "bad\nkey\nin\nmiddle");

        var ex = (await adapter.Invoking(a =>
                a.ListSinceAsync(null, 500, CancellationToken.None))
            .Should().ThrowAsync<AwCompletionsException>()).Subject.First();

        ex.IsTransient.Should().BeFalse();
        ex.Kind.Should().Be("invalid_api_key");
    }

    [Fact]
    public async Task ListSince_HttpStatus_401_LanzaPermanent()
    {
        var adapter = BuildAdapter(_ => CreateResponse(HttpStatusCode.Unauthorized, "auth required"));

        var ex = (await adapter.Invoking(a =>
                a.ListSinceAsync(null, 500, CancellationToken.None))
            .Should().ThrowAsync<AwCompletionsException>()).Subject.First();

        ex.IsTransient.Should().BeFalse();
        ex.Kind.Should().Be("http_401");
    }

    [Fact]
    public async Task ListSince_HttpStatus_400_LanzaPermanent()
    {
        var adapter = BuildAdapter(_ => CreateResponse(HttpStatusCode.BadRequest, "since inválido"));

        var ex = (await adapter.Invoking(a =>
                a.ListSinceAsync(null, 500, CancellationToken.None))
            .Should().ThrowAsync<AwCompletionsException>()).Subject.First();

        ex.IsTransient.Should().BeFalse();
        ex.Kind.Should().Be("http_400");
    }

    [Fact]
    public async Task ListSince_HttpStatus_503_LanzaTransient()
    {
        var adapter = BuildAdapter(_ => CreateResponse(HttpStatusCode.ServiceUnavailable, "down"));

        var ex = (await adapter.Invoking(a =>
                a.ListSinceAsync(null, 500, CancellationToken.None))
            .Should().ThrowAsync<AwCompletionsException>()).Subject.First();

        ex.IsTransient.Should().BeTrue();
        ex.Kind.Should().Be("http_5xx");
    }

    [Fact]
    public async Task ListSince_NetworkError_LanzaTransient()
    {
        var adapter = BuildAdapter(_ => throw new HttpRequestException("DNS failure"));

        var ex = (await adapter.Invoking(a =>
                a.ListSinceAsync(null, 500, CancellationToken.None))
            .Should().ThrowAsync<AwCompletionsException>()).Subject.First();

        ex.IsTransient.Should().BeTrue();
        ex.Kind.Should().Be("network");
    }

    [Fact]
    public async Task ListSince_BodyJsonInvalido_LanzaTransient()
    {
        var adapter = BuildAdapter(_ => CreateResponse(HttpStatusCode.OK, "<<no es json>>"));

        var ex = (await adapter.Invoking(a =>
                a.ListSinceAsync(null, 500, CancellationToken.None))
            .Should().ThrowAsync<AwCompletionsException>()).Subject.First();

        ex.IsTransient.Should().BeTrue();
        ex.Kind.Should().Be("invalid_response");
    }

    // ─── helpers ───

    private static HttpAwCompletionsAdapter BuildAdapter(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        string apiKey = "")
    {
        var http = new HttpClient(new StubHandler(handler))
        {
            BaseAddress = new Uri("http://localhost:5000"),
        };
        var factory = new SingleClientFactory(http);
        var options = Options.Create(new IntegracionesAwOptions
        {
            DropServiceApiKey = apiKey,
            DropTimeoutSeconds = 30,
        });
        return new HttpAwCompletionsAdapter(factory, options, NullLogger<HttpAwCompletionsAdapter>.Instance);
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) { _handler = handler; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public SingleClientFactory(HttpClient client) { _client = client; }
        public HttpClient CreateClient(string name) => _client;
    }
}
