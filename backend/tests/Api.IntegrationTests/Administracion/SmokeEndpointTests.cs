using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Tests integration del smoke endpoint del andamio de Administración
/// (F-Admin-PR1.2): <c>GET /api/v1/admin/smoke</c> requiere el permiso
/// canónico <c>admin.empresas.leer</c>.
///
/// <para>
/// Cubre los tres caminos:
/// <list type="bullet">
///   <item>Sin token → 401.</item>
///   <item>Con super-admin → 200 (tiene todos los permisos seedeados).</item>
///   <item>Con usuario auto-provisionado sin roles → 403.</item>
/// </list>
/// </para>
/// </summary>
public class SmokeEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "dev-admin-pr12-sin-permisos";

    private readonly WebApplicationFactory<Program> _factory;

    public SmokeEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Smoke_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/admin/smoke");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Smoke_Con_SuperAdmin_Retorna_200()
    {
        var client = await CreateClientAsync(SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");

        var response = await client.GetAsync("/api/v1/admin/smoke");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Smoke_Con_Usuario_Sin_Permiso_Retorna_403()
    {
        // Usuario auto-provisionado por fake-login sin asignación a ningún
        // rol que tenga admin.empresas.leer. Aunque tenga sesión válida,
        // PermissionAuthorizationHandler debe bloquear con 403.
        var client = await CreateClientAsync(
            SinPermisosOid,
            "sin-permisos@dev.local",
            "Usuario Sin Permisos");

        var response = await client.GetAsync("/api/v1/admin/smoke");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpClient> CreateClientAsync(string oid, string email, string nombre)
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, oid, email, nombre);
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
}
