using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Millet.Integraciones.Aw.Domain;

namespace Millet.Api.IntegrationTests.IntegracionesAw;

/// <summary>
/// Tests E2E del endpoint <c>/api/v1/integraciones/aw/cotizaciones</c>
/// (PR D). Levanta el host real con WebApplicationFactory y hace HTTP
/// contra el TestServer in-memory.
///
/// <para>
/// Cobertura inicial: auth (401 sin token), happy path POST + GET,
/// 409 quote_reference_duplicada, 404 detail no encontrada, paginación.
/// Los tests de transiciones complejas (Reintentar desde FailedDrop,
/// MarcarResuelto en estado equivocado) requieren manipular el estado
/// del aggregate en BD — se deja como
/// // PLATFORM-TODO(&lt;AwE2EStateSetup&gt;) para PR siguiente con un
/// helper de seed que ponga entidades en estado arbitrario via repo + bypass.
/// </para>
/// </summary>
public class CotizacionesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string EndpointBase = "/api/v1/integraciones/aw/cotizaciones";
    private const string SuperAdminOid = "dev-superadmin";

    private readonly WebApplicationFactory<Program> _factory;

    public CotizacionesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostCotizacion_SinToken_Retorna401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(EndpointBase, BuildSubmitBody());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCotizaciones_SinToken_Retorna401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(EndpointBase);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostCotizacion_HappyPath_Retorna202_ConId()
    {
        var client = await CreateSuperAdminClientAsync();
        var body = BuildSubmitBody();

        var response = await client.PostAsJsonAsync(EndpointBase, body);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var json = await ReadJsonAsync(response);
        var id = json.GetProperty("id").GetGuid();
        id.Should().NotBe(Guid.Empty);
        json.GetProperty("quoteReference").GetString().Should().Be(body.QuoteReference);
        // Enums salen como int (default ASP.NET Core JSON; Compras también).
        // EstadoEntidad.Submitted = 0.
        json.GetProperty("estado").GetInt32().Should().Be((int)EstadoEntidad.Submitted);
    }

    [Fact]
    public async Task PostCotizacion_QuoteReferenceDuplicada_Retorna409()
    {
        var client = await CreateSuperAdminClientAsync();
        var body = BuildSubmitBody();

        var primero = await client.PostAsJsonAsync(EndpointBase, body);
        Assert.Equal(HttpStatusCode.Accepted, primero.StatusCode);

        // Segundo POST con mismo QuoteReference (idem-key NUEVO para no caer
        // en el cache de idempotency middleware) debe ser 409.
        var clientFreshIdem = await CreateSuperAdminClientAsync();
        var segundo = await clientFreshIdem.PostAsJsonAsync(EndpointBase, body);

        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
        var json = await ReadJsonAsync(segundo);
        json.GetProperty("type").GetString()
            .Should().Be("https://millet-erp/errors/aw_quote_reference_duplicada");
        json.GetProperty("status").GetInt32().Should().Be(409);
    }

    [Fact]
    public async Task GetDetalle_IdNoExiste_Retorna404()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync($"{EndpointBase}/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCotizaciones_HappyPath_Retorna200_ConPagedResponse()
    {
        var client = await CreateSuperAdminClientAsync();
        // Crea 1 entrada para garantizar Total > 0.
        await client.PostAsJsonAsync(EndpointBase, BuildSubmitBody());

        var response = await client.GetAsync($"{EndpointBase}?limit=10&offset=0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        json.GetProperty("offset").GetInt32().Should().Be(0);
        json.GetProperty("limit").GetInt32().Should().Be(10);
        json.GetProperty("total").GetInt32().Should().BeGreaterThan(0);
        json.GetProperty("items").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetCotizaciones_FiltroEstado_FiltraCorrectamente()
    {
        var client = await CreateSuperAdminClientAsync();
        await client.PostAsJsonAsync(EndpointBase, BuildSubmitBody());

        // Query param también acepta el int del enum (binding por valor).
        var submittedInt = (int)EstadoEntidad.Submitted;
        var response = await client.GetAsync($"{EndpointBase}?estado={submittedInt}&limit=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items").EnumerateArray().ToList();
        items.Should().NotBeEmpty();
        foreach (var item in items)
        {
            item.GetProperty("estado").GetInt32().Should().Be(submittedInt);
        }
    }

    [Fact]
    public async Task PostReintentar_CotizacionEnSubmitted_Retorna409()
    {
        // Una cotización recién creada está en Submitted — reintentar no aplica.
        var client = await CreateSuperAdminClientAsync();
        var body = BuildSubmitBody();
        var post = await client.PostAsJsonAsync(EndpointBase, body);
        var id = (await ReadJsonAsync(post)).GetProperty("id").GetGuid();

        var response = await client.PostAsync($"{EndpointBase}/{id}/reintentar", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var json = await ReadJsonAsync(response);
        json.GetProperty("type").GetString()
            .Should().Be("https://millet-erp/errors/aw_invalid_state_transition");
    }

    // ─── Helpers ───

    // Seed inicial por proceso de test (ticks % 90000 + 5000 → garantiza
    // 5 dígitos sin desbordar). Sin esto, runs repetidos del test reutilizan
    // Q-2026-00001 y caen en quote_reference_duplicada de runs previos.
    private static int _counter = (int)(DateTimeOffset.UtcNow.Ticks % 90000L) + 5000;

    private static SubmitBody BuildSubmitBody()
    {
        // QuoteReference debe matchear ^Q-\d{4}-\d{5}$ (RegistrarCotizacionEdiValidator).
        // Counter se inicializa per-proceso con seed basado en ticks; dentro
        // del proceso Interlocked garantiza unicidad.
        var n = Interlocked.Increment(ref _counter) % 100000;
        var quoteRef = $"Q-2026-{n:D5}";

        // EdiContent: >=100 chars y debe contener "#END#" literal.
        var ediContent = new string('A', 200) + "#END#";

        return new SubmitBody(
            QuoteReference: quoteRef,
            Sucursal: "CIR",
            EdiContent: ediContent,
            CustomerTaxId: "EXT", // literal permitido (cliente sin RFC mexicano)
            CustomerName: "Cliente Test",
            Source: "glass_agent",
            ItemsCount: 1,
            PayloadOriginalJson: "{\"k\":\"v\"}");
    }

    private sealed record SubmitBody(
        string QuoteReference,
        string Sucursal,
        string EdiContent,
        string CustomerTaxId,
        string CustomerName,
        string Source,
        int ItemsCount,
        string PayloadOriginalJson);

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        // Usa el helper TestClientExtensions para auto-inyectar Idempotency-Key
        // en mutaciones (POST endpoints declaran [RequireIdempotencyKey]).
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(_factory.CreateClient());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> FakeLoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = SuperAdminOid,
            Email = "superadmin@dev.local",
            Nombre = "Super Admin Dev",
            EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("fake-login no devolvió accessToken");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        return (await JsonDocument.ParseAsync(stream)).RootElement.Clone();
    }
}
