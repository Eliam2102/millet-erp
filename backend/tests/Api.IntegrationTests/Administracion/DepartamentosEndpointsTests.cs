using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Tests integration del CRUD de Departamentos (F-Admin-PR2.3).
/// Endpoint: <c>/api/v1/admin/departamentos</c>.
/// </summary>
public class DepartamentosEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string EndpointBase = "/api/v1/admin/departamentos";

    private readonly WebApplicationFactory<Program> _factory;

    public DepartamentosEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Crear_Departamento_Con_Datos_Validos_Retorna_201()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = RandomClave("DEPT");

        var response = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = "Departamento Test Nuevo",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(clave, body.GetProperty("clave").GetString());
        Assert.NotEqual(Guid.Empty, body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Crear_Departamento_Con_Clave_Duplicada_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = RandomClave("DEPD");

        var primero = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = "Departamento Original",
        });
        primero.EnsureSuccessStatusCode();

        var duplicado = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = "Departamento Duplicado",
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);
    }

    [Fact]
    public async Task Crear_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            Clave = "ANY",
            Nombre = "x",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Helpers ---

    private static string RandomClave(string prefix)
    {
        var hex = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        return $"{prefix}-{hex}";
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
