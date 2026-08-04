using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Almacen.Salidas;

/// <summary>
/// Tests del endpoint ABIERTO de CC-Máquina para el vale (Fase E PR5):
/// <c>GET /api/v1/almacen/salidas/dim3/buscar</c>. Gemelo bajo Almacén del
/// endpoint de OC (PR3), gateado por <c>almacen.salidas.por-vale</c>. El
/// backend abre el selector — un usuario sin el permiso recibe 403 y no puede
/// saltarse el alcance por la URL. Devuelve TODAS las activas sin filtro de
/// alcance (contraste con <c>/api/v1/centros-costo/dim3/buscar</c>, filtrado).
/// </summary>
public class Dim3BuscarAbiertoSalidaEndpointTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms";
    private const string Endpoint = "/api/v1/almacen/salidas/dim3/buscar";

    private readonly WebApplicationFactory<Program> _factory;

    public Dim3BuscarAbiertoSalidaEndpointTests(WebApplicationFactory<Program> factory)
        => _factory = factory;

    [Fact]
    public async Task Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(Endpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Sin_PorVale_Retorna_403()
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Con_PorVale_Retorna_200_ConArray()
    {
        // SuperAdmin tiene almacen.salidas.por-vale → 200 + array (activas, sin alcance).
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync(Endpoint + "?limit=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
    }

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

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
