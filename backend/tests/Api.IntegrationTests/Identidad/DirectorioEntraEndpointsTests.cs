using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Identidad;

/// <summary>
/// Validación en línea del correo corporativo contra el directorio Entra
/// simulado (plan 15, F2). Usa las cuentas semilla y los dominios de
/// <c>appsettings.Development.json</c> (<c>Entra</c>).
/// </summary>
public class DirectorioEntraEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms";
    private const string ValidarEndpoint = "/api/v1/identidad/directorio-entra/validar-correo";

    private readonly WebApplicationFactory<Program> _factory;

    public DirectorioEntraEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"{ValidarEndpoint}?correo=ana.lopez@millet.mx");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Sin_Permiso_Retorna_403()
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"{ValidarEndpoint}?correo=ana.lopez@millet.mx");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cuenta_Existente_Se_Puede_Vincular_Y_No_Crear()
    {
        var client = await CreateSuperAdminClientAsync();

        var json = await ValidarAsync(client, "Ana.Lopez@millet.mx");

        Assert.True(json.GetProperty("dominioPermitido").GetBoolean());
        var cuenta = json.GetProperty("cuentaEntra");
        Assert.Equal("Ana López", cuenta.GetProperty("nombreMostrado").GetString());
        Assert.True(Guid.TryParse(cuenta.GetProperty("objectId").GetString(), out _));
        Assert.True(json.GetProperty("puedeVincularCuentaExistente").GetBoolean());
        Assert.False(json.GetProperty("puedeCrearCuentaNueva").GetBoolean());
    }

    [Fact]
    public async Task Correo_Libre_En_Dominio_Permitido_Se_Puede_Crear()
    {
        var client = await CreateSuperAdminClientAsync();

        var json = await ValidarAsync(client, $"libre-{RandomSufijo()}@millet.mx");

        Assert.True(json.GetProperty("dominioPermitido").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("cuentaEntra").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("usuarioErp").ValueKind);
        Assert.False(json.GetProperty("puedeVincularCuentaExistente").GetBoolean());
        Assert.True(json.GetProperty("puedeCrearCuentaNueva").GetBoolean());
    }

    [Fact]
    public async Task Cuenta_Deshabilitada_No_Se_Puede_Vincular()
    {
        var client = await CreateSuperAdminClientAsync();

        var json = await ValidarAsync(client, "baja.ejemplo@millet.mx");

        Assert.False(json.GetProperty("cuentaEntra").GetProperty("habilitada").GetBoolean());
        Assert.False(json.GetProperty("puedeVincularCuentaExistente").GetBoolean());
        Assert.False(json.GetProperty("puedeCrearCuentaNueva").GetBoolean());
    }

    [Fact]
    public async Task Dominio_No_Permitido_Bloquea_Ambos_Caminos()
    {
        var client = await CreateSuperAdminClientAsync();

        var json = await ValidarAsync(client, $"externo-{RandomSufijo()}@gmail.com");

        Assert.False(json.GetProperty("dominioPermitido").GetBoolean());
        Assert.False(json.GetProperty("puedeVincularCuentaExistente").GetBoolean());
        Assert.False(json.GetProperty("puedeCrearCuentaNueva").GetBoolean());
    }

    [Fact]
    public async Task Usuario_Erp_Con_Mismo_Correo_Se_Informa_Y_Bloquea_Crear()
    {
        var client = await CreateSuperAdminClientAsync();
        var correo = $"existente-{RandomSufijo()}@millet.mx";
        var crear = await client.PostAsJsonAsync("/api/v1/identidad/usuarios", new
        {
            Id = Guid.Empty,
            Email = correo,
            EntraIdObjectId = (string?)null,
            NombreCompleto = "Usuario Existente",
            DepartamentoId = (Guid?)null,
        });
        Assert.Equal(HttpStatusCode.Created, crear.StatusCode);
        var usuarioId = (await ReadJsonAsync(crear)).GetProperty("id").GetGuid();

        var json = await ValidarAsync(client, correo);

        var usuario = json.GetProperty("usuarioErp");
        Assert.Equal(usuarioId, usuario.GetProperty("id").GetGuid());
        // Enum serializado como número: 1 = PendientePrimerAcceso.
        Assert.Equal(1, usuario.GetProperty("estadoAcceso").GetInt32());
        Assert.False(json.GetProperty("puedeCrearCuentaNueva").GetBoolean());
    }

    [Theory]
    [InlineData("")]
    [InlineData("sin-arroba")]
    public async Task Correo_Invalido_Retorna_400(string correo)
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync($"{ValidarEndpoint}?correo={Uri.EscapeDataString(correo)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<JsonElement> ValidarAsync(HttpClient client, string correo)
    {
        var response = await client.GetAsync($"{ValidarEndpoint}?correo={Uri.EscapeDataString(correo)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string RandomSufijo() => Guid.NewGuid().ToString("N")[..8];

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
