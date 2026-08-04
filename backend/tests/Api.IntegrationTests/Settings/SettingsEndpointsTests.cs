using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Settings;

/// <summary>
/// Tests integration de los endpoints genéricos
/// <c>/api/v1/{modulo}/settings/*</c> introducidos por F-Admin-PR1.1
/// (ADR-0034, A7=b). El módulo Compras es el exemplar — su provider
/// expone <c>AutoGenerarOcAlAutorizar</c> con <c>Mostrar=Custom</c>.
///
/// <para>
/// Estos tests viven en <c>Api.IntegrationTests</c> con la
/// <see cref="WebApplicationFactory{TEntryPoint}"/> default (no stubs)
/// para validar el wiring real del endpoint + provider + Identidad +
/// idempotency middleware.
/// </para>
/// </summary>
public class SettingsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";

    private readonly WebApplicationFactory<Program> _factory;

    public SettingsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Schema_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/compras/settings/schema");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Schema_De_Modulo_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync("/api/v1/no-existe/settings/schema");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Schema_Compras_Retorna_AutoGenerarOcAlAutorizar()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync("/api/v1/compras/settings/schema");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);

        var items = body.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1, "El schema de Compras debe tener al menos un item");

        var auto = items.EnumerateArray()
            .FirstOrDefault(i => i.GetProperty("clave").GetString() == "AutoGenerarOcAlAutorizar");
        Assert.NotEqual(default, auto);

        // Tipo enum Booleano (=0).
        Assert.Equal(0, auto.GetProperty("tipo").GetInt32());

        // Modo Custom (=1) con RutaCustom.
        Assert.Equal(1, auto.GetProperty("mostrar").GetInt32());
        Assert.Equal("/compras/configuracion", auto.GetProperty("rutaCustom").GetString());

        // Permisos canónicos.
        Assert.Equal("compras.configuracion.leer", auto.GetProperty("permisoLeer").GetString());
        Assert.Equal("compras.configuracion.editar", auto.GetProperty("permisoEditar").GetString());

        // Valor actual presente (true | false).
        var valor = auto.GetProperty("valor");
        Assert.True(valor.ValueKind is JsonValueKind.True or JsonValueKind.False);
    }

    [Fact]
    public async Task Patch_Setting_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        // Idempotency middleware exige el header en PATCH/POST.
        client.DefaultRequestHeaders.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.PatchAsJsonAsync(
            "/api/v1/compras/settings/AutoGenerarOcAlAutorizar",
            new { valor = true });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Patch_Setting_De_Clave_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.PatchAsJsonAsync(
            "/api/v1/compras/settings/NoExiste",
            new { valor = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_Setting_Con_Tipo_Invalido_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();

        // AutoGenerarOcAlAutorizar es Booleano; mandar string debe fallar 422.
        var response = await client.PatchAsJsonAsync(
            "/api/v1/compras/settings/AutoGenerarOcAlAutorizar",
            new { valor = "no-es-bool" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Patch_Setting_Compras_Aplica_Y_GetSchema_Lo_Refleja()
    {
        var client = await CreateSuperAdminClientAsync();

        // Leer estado inicial.
        var initialSchema = await client.GetAsync("/api/v1/compras/settings/schema");
        initialSchema.EnsureSuccessStatusCode();
        var initialBody = await ReadJsonAsync(initialSchema);
        var initialValor = initialBody.GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("clave").GetString() == "AutoGenerarOcAlAutorizar")
            .GetProperty("valor").GetBoolean();

        // PATCH al valor opuesto.
        var nuevoValor = !initialValor;
        var patch = await client.PatchAsJsonAsync(
            "/api/v1/compras/settings/AutoGenerarOcAlAutorizar",
            new { valor = nuevoValor });
        patch.EnsureSuccessStatusCode();

        // El response del PATCH retorna el SettingItem actualizado.
        var patchBody = await ReadJsonAsync(patch);
        Assert.Equal("AutoGenerarOcAlAutorizar", patchBody.GetProperty("clave").GetString());
        Assert.Equal(nuevoValor, patchBody.GetProperty("valor").GetBoolean());

        // GET schema posterior lo refleja.
        var verifySchema = await client.GetAsync("/api/v1/compras/settings/schema");
        verifySchema.EnsureSuccessStatusCode();
        var verifyBody = await ReadJsonAsync(verifySchema);
        var verifyValor = verifyBody.GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("clave").GetString() == "AutoGenerarOcAlAutorizar")
            .GetProperty("valor").GetBoolean();
        Assert.Equal(nuevoValor, verifyValor);

        // Cleanup: restaurar para no contaminar otros tests.
        var restore = await client.PatchAsJsonAsync(
            "/api/v1/compras/settings/AutoGenerarOcAlAutorizar",
            new { valor = initialValor });
        restore.EnsureSuccessStatusCode();
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
