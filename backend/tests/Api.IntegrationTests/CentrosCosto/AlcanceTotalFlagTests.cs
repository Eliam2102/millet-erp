using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.CentrosCosto;

/// <summary>
/// El flag <c>esAlcanceTotal</c> del árbol de asignación (CECO-FE-PR3) se
/// resuelve en el ENDPOINT vía <c>IPermissionLoader</c> del usuario
/// SELECCIONADO — no en el módulo CentrosCosto (que sigue sin dependencia
/// de Identidad, #620). Los tests de handler vía <c>IMediator</c> NO tocan
/// esa resolución; por eso este test golpea el endpoint HTTP.
///
/// <para>Barato a propósito: el SuperAdmin ya tiene TODOS los permisos
/// canónicos (incluido <c>centros_costo.dim3.leer-todos</c>), así que el
/// caso <c>true</c> es su propio userId; el <c>false</c> es un Guid
/// inexistente (el loader devuelve permisos vacíos). Sin sembrar rol ni
/// permiso custom.</para>
/// </summary>
public class AlcanceTotalFlagTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";

    private readonly WebApplicationFactory<Program> _factory;

    public AlcanceTotalFlagTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Usuario_con_leer_todos_tiene_esAlcanceTotal_true()
    {
        var (client, superAdminId) = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync(
            $"/api/v1/centros-costo/asignaciones/{superAdminId}/arbol");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var root = await ReadJsonAsync(response);
        Assert.True(root.GetProperty("esAlcanceTotal").GetBoolean());
    }

    [Fact]
    public async Task Usuario_sin_permiso_tiene_esAlcanceTotal_false()
    {
        var (client, _) = await CreateSuperAdminClientAsync();

        // Un usuario inexistente: el loader devuelve permisos vacíos → el
        // flag es false. El árbol se devuelve igual (todo en Ninguno).
        var response = await client.GetAsync(
            $"/api/v1/centros-costo/asignaciones/{Guid.NewGuid()}/arbol");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var root = await ReadJsonAsync(response);
        Assert.False(root.GetProperty("esAlcanceTotal").GetBoolean());
    }

    private async Task<(HttpClient Client, Guid UsuarioId)> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = SuperAdminOid,
            Email = "superadmin@dev.local",
            Nombre = "Super Admin Dev",
            EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();

        var root = await ReadJsonAsync(response);
        var token = root.GetProperty("accessToken").GetString()
            ?? throw new InvalidOperationException("fake-login no devolvió accessToken");
        var usuarioId = root.GetProperty("usuario").GetProperty("id").GetGuid();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return (client, usuarioId);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
