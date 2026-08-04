using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Compras;

/// <summary>
/// Tests integration de los endpoints de configuración del módulo Compras
/// (PR-A 2026-05-13): <c>GET /api/v1/compras/configuracion</c>,
/// <c>PATCH /api/v1/compras/configuracion</c>, y la inclusión de
/// <c>comprasSettings</c> en el payload de <c>/api/auth/me</c>.
///
/// <para>
/// Estos tests viven en <c>Api.IntegrationTests</c> (no
/// <c>Compras.IntegrationTests</c>) porque la
/// <see cref="WebApplicationFactory{TEntryPoint}"/> default no aplica
/// el seed de <see cref="Millet.Compras.IntegrationTests.StubsWebApplicationFactory"/>
/// que fuerza <c>AutoGenerarOcAlAutorizar=true</c> globalmente.
/// </para>
/// </summary>
public class ComprasSettingsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string EndpointBase = "/api/v1/compras/configuracion";

    private readonly WebApplicationFactory<Program> _factory;

    public ComprasSettingsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(EndpointBase);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Con_SuperAdmin_Retorna_Configuracion()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync(EndpointBase);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.NotEqual(Guid.Empty, body.GetProperty("empresaId").GetGuid());
        Assert.True(body.TryGetProperty("autoGenerarOcAlAutorizar", out _));
    }

    [Fact]
    public async Task Patch_Establece_Flag_Y_Get_Posterior_Lo_Refleja()
    {
        var client = await CreateSuperAdminClientAsync();

        // Estado inicial: leer la config.
        var initialGet = await client.GetAsync(EndpointBase);
        initialGet.EnsureSuccessStatusCode();
        var initialBody = await ReadJsonAsync(initialGet);
        var initialFlag = initialBody.GetProperty("autoGenerarOcAlAutorizar").GetBoolean();

        // Invertir el flag con PATCH.
        var nuevoValor = !initialFlag;
        var patch = await client.PatchAsJsonAsync(
            EndpointBase,
            new { AutoGenerarOcAlAutorizar = nuevoValor });
        patch.EnsureSuccessStatusCode();

        var patchBody = await ReadJsonAsync(patch);
        Assert.Equal(nuevoValor, patchBody.GetProperty("autoGenerarOcAlAutorizar").GetBoolean());

        // Verificación: nuevo GET refleja el cambio.
        var verify = await client.GetAsync(EndpointBase);
        verify.EnsureSuccessStatusCode();
        var verifyBody = await ReadJsonAsync(verify);
        Assert.Equal(nuevoValor, verifyBody.GetProperty("autoGenerarOcAlAutorizar").GetBoolean());

        // Restaurar el estado original para no afectar a otros tests.
        var restore = await client.PatchAsJsonAsync(
            EndpointBase,
            new { AutoGenerarOcAlAutorizar = initialFlag });
        restore.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Me_Incluye_ComprasSettings_Cuando_Hay_Empresa()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync("/api/auth/me");
        response.EnsureSuccessStatusCode();

        var body = await ReadJsonAsync(response);
        Assert.True(body.TryGetProperty("comprasSettings", out var settings),
            "MeResponse debe incluir el campo comprasSettings");
        Assert.NotEqual(JsonValueKind.Null, settings.ValueKind);
        Assert.NotEqual(Guid.Empty, settings.GetProperty("empresaId").GetGuid());
        Assert.True(settings.TryGetProperty("autoGenerarOcAlAutorizar", out _));
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
