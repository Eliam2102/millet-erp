using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Infrastructure.Adapters;

namespace Millet.Integraciones.Aw.UnitTests.Adapters;

/// <summary>
/// Tests del clasificador de status HTTP del <see cref="HttpAwDropAdapter"/>.
/// La SUT genera <see cref="AwDropException"/> con <c>IsTransient</c> y
/// <c>Kind</c> según la response.
/// </summary>
public sealed class HttpAwDropAdapterTests
{
    [Fact]
    public async Task SendEdi_HttpStatus_200_RetornaDropResultConBytes()
    {
        var adapter = BuildAdapter(_ => CreateResponse(HttpStatusCode.OK));
        var content = "EDI-PAYLOAD-123";

        var result = await adapter.SendEdiAsync("test.edi", content, CancellationToken.None);

        result.Should().NotBeNull();
        result.BytesWritten.Should().Be(content.Length);
    }

    [Fact]
    public async Task SendEdi_HttpStatus_401_LanzaPermanent()
    {
        var adapter = BuildAdapter(_ => CreateResponse(HttpStatusCode.Unauthorized, "auth required"));

        var act = () => adapter.SendEdiAsync("a.edi", "X", CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<AwDropException>()).Subject.First();
        ex.IsTransient.Should().BeFalse();
        ex.Kind.Should().Be("http_401");
    }

    [Fact]
    public async Task SendEdi_HttpStatus_422_LanzaPermanent()
    {
        var adapter = BuildAdapter(_ => CreateResponse(HttpStatusCode.UnprocessableEntity, "bad payload"));

        var ex = (await adapter.Invoking(a => a.SendEdiAsync("a", "x", CancellationToken.None))
            .Should().ThrowAsync<AwDropException>()).Subject.First();

        ex.IsTransient.Should().BeFalse();
        ex.Kind.Should().Be("http_422");
    }

    [Fact]
    public async Task SendEdi_HttpStatus_503_LanzaTransient()
    {
        var adapter = BuildAdapter(_ => CreateResponse(HttpStatusCode.ServiceUnavailable, "down"));

        var ex = (await adapter.Invoking(a => a.SendEdiAsync("a", "x", CancellationToken.None))
            .Should().ThrowAsync<AwDropException>()).Subject.First();

        ex.IsTransient.Should().BeTrue();
        ex.Kind.Should().Be("http_5xx");
    }

    [Fact]
    public async Task SendEdi_HttpStatus_429_LanzaTransient()
    {
        var adapter = BuildAdapter(_ => CreateResponse(HttpStatusCode.TooManyRequests));

        var ex = (await adapter.Invoking(a => a.SendEdiAsync("a", "x", CancellationToken.None))
            .Should().ThrowAsync<AwDropException>()).Subject.First();

        ex.IsTransient.Should().BeTrue();
        ex.Kind.Should().Be("http_429");
    }

    [Fact]
    public async Task SendEdi_NetworkError_LanzaTransient()
    {
        var adapter = BuildAdapter(_ => throw new HttpRequestException("DNS failure"));

        var ex = (await adapter.Invoking(a => a.SendEdiAsync("a", "x", CancellationToken.None))
            .Should().ThrowAsync<AwDropException>()).Subject.First();

        ex.IsTransient.Should().BeTrue();
        ex.Kind.Should().Be("network");
    }

    [Fact]
    public async Task SendEdi_FilenameVacio_LanzaArgumentException()
    {
        var adapter = BuildAdapter(_ => CreateResponse(HttpStatusCode.OK));

        await adapter.Invoking(a => a.SendEdiAsync("", "x", CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendEdi_EnviaHeaderXFilename()
    {
        string? capturedFilename = null;
        var adapter = BuildAdapter(req =>
        {
            capturedFilename = req.Headers.GetValues("X-Filename").FirstOrDefault();
            return CreateResponse(HttpStatusCode.OK);
        });

        await adapter.SendEdiAsync("Q-001.edi", "EDI", CancellationToken.None);

        capturedFilename.Should().Be("Q-001.edi");
    }

    [Fact]
    public async Task SendEdi_ConApiKey_EnviaHeaderXApiKey()
    {
        string? capturedApiKey = null;
        var adapter = BuildAdapter(
            handler: req =>
            {
                capturedApiKey = req.Headers.GetValues("X-API-Key").FirstOrDefault();
                return CreateResponse(HttpStatusCode.OK);
            },
            apiKey: "secret-key-123");

        await adapter.SendEdiAsync("a.edi", "x", CancellationToken.None);

        capturedApiKey.Should().Be("secret-key-123");
    }

    [Fact]
    public async Task SendEdi_SinApiKey_NoEnviaHeader()
    {
        bool hasApiKey = true;
        var adapter = BuildAdapter(
            handler: req =>
            {
                hasApiKey = req.Headers.Contains("X-API-Key");
                return CreateResponse(HttpStatusCode.OK);
            },
            apiKey: "");

        await adapter.SendEdiAsync("a.edi", "x", CancellationToken.None);

        hasApiKey.Should().BeFalse();
    }

    [Fact]
    public async Task SendEdi_ApiKeyConTrailingNewlines_LaTrimea()
    {
        // Bug E2E PR D: secret en KV con "\r\r\n" trailing rompía Headers.Add.
        // El Trim defensivo debe aceptar el secret tolerando ruido al final.
        string? capturedApiKey = null;
        var adapter = BuildAdapter(
            handler: req =>
            {
                capturedApiKey = req.Headers.GetValues("X-API-Key").FirstOrDefault();
                return CreateResponse(HttpStatusCode.OK);
            },
            apiKey: "real-secret-value\r\r\n");

        await adapter.SendEdiAsync("a.edi", "x", CancellationToken.None);

        capturedApiKey.Should().Be("real-secret-value");
    }

    [Fact]
    public async Task SendEdi_ApiKeyConCharsInvalidosEnMedio_LanzaAwDropExceptionPermanent()
    {
        // Si el secret tiene chars de control en medio (no solo trailing),
        // Trim no ayuda — Headers.Add lanza InvalidOperationException. El
        // adapter debe envolver y lanzar AwDropException IsTransient=false
        // con kind=invalid_api_key (mejor que el catch general silencioso).
        var adapter = BuildAdapter(
            handler: _ => CreateResponse(HttpStatusCode.OK),
            apiKey: "bad\nkey\nin\nmiddle");

        var ex = (await adapter.Invoking(a =>
                a.SendEdiAsync("a.edi", "x", CancellationToken.None))
            .Should().ThrowAsync<AwDropException>()).Subject.First();

        ex.IsTransient.Should().BeFalse();
        ex.Kind.Should().Be("invalid_api_key");
    }

    [Fact]
    public async Task SendEdi_FilenameConCharsInvalidos_LanzaAwDropExceptionPermanent()
    {
        var adapter = BuildAdapter(_ => CreateResponse(HttpStatusCode.OK));

        var ex = (await adapter.Invoking(a =>
                a.SendEdiAsync("bad\r\nfilename.edi", "x", CancellationToken.None))
            .Should().ThrowAsync<AwDropException>()).Subject.First();

        ex.IsTransient.Should().BeFalse();
        ex.Kind.Should().Be("invalid_filename");
    }

    // ─── helpers ───

    private static HttpAwDropAdapter BuildAdapter(
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
        return new HttpAwDropAdapter(factory, options, NullLogger<HttpAwDropAdapter>.Instance);
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode status, string body = "ok")
    {
        var msg = new HttpResponseMessage(status)
        {
            Content = new StringContent(body),
        };
        return msg;
    }

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
