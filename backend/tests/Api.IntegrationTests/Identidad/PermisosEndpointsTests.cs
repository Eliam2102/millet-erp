using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Identidad;

/// <summary>
/// Tests integration de <c>GET /api/v1/identidad/permisos</c>
/// (F-Admin-PR3.2). Verifica que sin <c>?agrupado=true</c> retorna lista
/// plana y con <c>?agrupado=true</c> también incluye agrupación por módulo.
/// </summary>
public class PermisosEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string Endpoint = "/api/v1/identidad/permisos";

    private readonly WebApplicationFactory<Program> _factory;

    public PermisosEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Listar_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(Endpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Listar_Sin_Agrupado_Retorna_Lista_Plana()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var items = body.GetProperty("items");
        Assert.True(items.GetArrayLength() > 0, "Debe haber permisos seedeados.");

        // `grupos` queda en null o no aparece cuando agrupado=false.
        if (body.TryGetProperty("grupos", out var grupos))
        {
            Assert.Equal(JsonValueKind.Null, grupos.ValueKind);
        }

        // Cada permiso expone los campos del shape público.
        var primero = items[0];
        Assert.True(primero.TryGetProperty("id", out _));
        Assert.True(primero.TryGetProperty("codigo", out _));
        Assert.True(primero.TryGetProperty("modulo", out _));
        Assert.True(primero.TryGetProperty("recurso", out _));
        Assert.True(primero.TryGetProperty("accion", out _));
    }

    [Fact]
    public async Task Listar_Con_Agrupado_True_Incluye_Grupos_Por_Modulo()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync(Endpoint + "?agrupado=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);

        Assert.True(body.TryGetProperty("grupos", out var grupos),
            "Con agrupado=true debe venir el campo `grupos`.");
        Assert.Equal(JsonValueKind.Array, grupos.ValueKind);
        Assert.True(grupos.GetArrayLength() >= 1,
            "Debe haber al menos un módulo (identidad/admin/compras).");

        // Cada grupo debe tener `modulo` y `items`.
        foreach (var grupo in grupos.EnumerateArray())
        {
            Assert.True(grupo.TryGetProperty("modulo", out var modulo));
            Assert.True(grupo.TryGetProperty("items", out var grpItems));
            Assert.False(string.IsNullOrWhiteSpace(modulo.GetString()));
            Assert.True(grpItems.GetArrayLength() >= 1);
        }
    }

    // --- Helpers ---

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
