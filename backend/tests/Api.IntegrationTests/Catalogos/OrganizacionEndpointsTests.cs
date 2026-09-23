using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Catalogos;

/// <summary>
/// Tests integration de los 3 endpoints de catálogos organizacionales
/// (B.1): sucursales, departamentos, almacenes. Asume que el seed
/// <c>CatalogosTestSeedHostedService</c> sembró 3+5+4 filas con claves
/// deterministas (MID/MTY/QRO, COMPRAS/ALMACEN/MTTO/ING/CAL,
/// ALM-MID-G/ALM-MID-MP/ALM-MTY-G/ALM-QRO-G).
/// </summary>
public class OrganizacionEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms";
    private static readonly Guid SucursalMidId = Guid.Parse("00000005-0003-0000-0000-000000000001");

    private readonly WebApplicationFactory<Program> _factory;

    public OrganizacionEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // --- Sucursales ---

    [Fact]
    public async Task ListarSucursales_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/catalogos/sucursales");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListarSucursales_Sin_Permiso_Retorna_403()
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/catalogos/sucursales");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListarSucursales_Con_Permiso_Retorna_Las_3_Seedeadas()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync("/api/v1/catalogos/sucursales");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items");
        var claves = items.EnumerateArray().Select(s => s.GetProperty("clave").GetString()).ToList();
        Assert.Contains("MID", claves);
        Assert.Contains("MTY", claves);
        Assert.Contains("QRO", claves);
    }

    [Fact]
    public async Task ListarSucursales_Filtra_Por_Q_Substring()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync("/api/v1/catalogos/sucursales?q=Monterrey");

        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("MTY", items[0].GetProperty("clave").GetString());
    }

    // --- Departamentos ---

    [Fact]
    public async Task ListarDepartamentos_Con_Permiso_Retorna_Los_5_Seedeados()
    {
        var client = await CreateSuperAdminClientAsync();

        // Se busca cada clave sembrada con ?q=: la BD de dev acumula
        // departamentos de otras pruebas que ordenan antes y sacaban a los
        // sembrados de la primera página.
        foreach (var clave in new[] { "COMPRAS", "ALMACEN", "MTTO", "ING", "CAL" })
        {
            var response = await client.GetAsync($"/api/v1/catalogos/departamentos?q={clave}&limit=200");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await ReadJsonAsync(response);
            var claves = json.GetProperty("items").EnumerateArray()
                .Select(d => d.GetProperty("clave").GetString())
                .ToList();
            Assert.Contains(clave, claves);
        }
    }

    // --- Almacenes ---

    [Fact]
    public async Task ListarAlmacenes_Con_Permiso_Retorna_Todos()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync("/api/v1/catalogos/almacenes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.True(json.GetProperty("total").GetInt32() >= 4);
    }

    [Fact]
    public async Task ListarAlmacenes_Filtra_Por_SucursalId_Devuelve_Solo_Los_De_Esa_Sucursal()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync(
            $"/api/v1/catalogos/almacenes?sucursalId={SucursalMidId}");

        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items");
        // MID tiene 2 almacenes en el seed (ALM-MID-G + ALM-MID-MP).
        Assert.True(items.GetArrayLength() >= 2);
        foreach (var item in items.EnumerateArray())
        {
            Assert.Equal(SucursalMidId, item.GetProperty("sucursalId").GetGuid());
        }
    }

    // --- Helpers ---

    // Empresa donde viven los catálogos sembrados. Sin fijarla, el login cae
    // en la primera empresa del super-admin, y la BD de dev acumula empresas
    // de prueba a las que otras suites lo asignan.
    private static readonly Guid EmpresaSeedId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev", EmpresaSeedId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> FakeLoginAsync(
        HttpClient client, string oid, string email, string nombre, Guid? empresaId = null)
    {
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid,
            Email = email,
            Nombre = nombre,
            EmpresaId = empresaId,
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
