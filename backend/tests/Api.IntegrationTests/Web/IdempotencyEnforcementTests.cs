using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Millet.Api.IntegrationTests.Fixtures;

namespace Millet.Api.IntegrationTests.Web;

/// <summary>
/// Tests confirmatorios (F8-PR2) que verifican que cada endpoint POST de
/// mutación de Compras decorado con <c>[RequireIdempotencyKey]</c> rechaza
/// requests autenticados sin header con <c>400 MISSING_IDEMPOTENCY_KEY</c>.
/// Smoke check del wireado de la decoración + middleware.
/// </summary>
public class IdempotencyEnforcementTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";

    private readonly WebApplicationFactory<Program> _factory;

    public IdempotencyEnforcementTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Crear_SinIdempotencyKey_Retorna_400_MISSING()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/compras/requisiciones", BuildCrearBody());

        await AssertMissingIdempotencyKeyAsync(response);
    }

    [Fact]
    public async Task AgregarLinea_SinIdempotencyKey_Retorna_400_MISSING()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/lineas",
            new { ArticuloId = Guid.NewGuid(), Cantidad = 1m, UnidadMedida = "PZA",
                  PrecioEstimadoMonto = 1m, PrecioEstimadoMoneda = "MXN" });

        await AssertMissingIdempotencyKeyAsync(response);
    }

    [Fact]
    public async Task Transmitir_SinIdempotencyKey_Retorna_400_MISSING()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/transmitir", content: null);

        await AssertMissingIdempotencyKeyAsync(response);
    }

    [Fact]
    public async Task Autorizar_SinIdempotencyKey_Retorna_400_MISSING()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/autorizaciones",
            new { Nivel = 1, Notas = (string?)null });

        await AssertMissingIdempotencyKeyAsync(response);
    }

    [Fact]
    public async Task Rechazar_SinIdempotencyKey_Retorna_400_MISSING()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/rechazar",
            new { MotivoId = Guid.NewGuid(), MotivoTexto = (string?)null });

        await AssertMissingIdempotencyKeyAsync(response);
    }

    [Fact]
    public async Task Cancelar_SinIdempotencyKey_Retorna_400_MISSING()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/cancelar",
            new { MotivoId = Guid.NewGuid(), MotivoTexto = (string?)null });

        await AssertMissingIdempotencyKeyAsync(response);
    }

    // --- Helpers ---

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        // Cliente "raw" sin auto-injection de Idempotency-Key — los tests
        // de este archivo validan precisamente que la falta del header
        // produce 400.
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task AssertMissingIdempotencyKeyAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("MISSING_IDEMPOTENCY_KEY", json.GetProperty("code").GetString());
    }

    // Estos tests fallan en el middleware Idempotency-Key antes de llegar
    // al handler, así que los IDs del body son irrelevantes — usar el
    // fixture canónico evita ruido.
    private static CrearRequisicionRequestBody BuildCrearBody() =>
        TestComprasFixtures.BuildCrearRqValidBody(descripcion: "smoke F8-PR2");

    private static async Task<string> FakeLoginAsync(HttpClient client, string oid, string email, string nombre)
    {
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid,
            Email = email,
            Nombre = nombre,
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
        var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
