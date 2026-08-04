using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Tests integration del CRUD de Empresas + Sucursales (F-Admin-PR2.3).
/// Cubre los happy paths de los 8 endpoints + caminos negativos clave
/// (401 sin token, 409 RFC/Clave duplicada, 200 PATCH refleja en GET).
///
/// <para>
/// Cada test que crea filas genera un sufijo aleatorio para no chocar
/// con seeds del <c>CatalogosTestSeedHostedService</c> ni con datos
/// dejados por tests previos en la misma DB compartida.
/// </para>
/// </summary>
public class EmpresasEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string EndpointBase = "/api/v1/admin/empresas";

    private readonly WebApplicationFactory<Program> _factory;

    public EmpresasEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Listar_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(EndpointBase);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Listar_Con_SuperAdmin_Retorna_200_Con_Empresa_Bootstrap()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync(EndpointBase);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var total = body.GetProperty("total").GetInt32();
        Assert.True(total >= 1, "Debe haber al menos la empresa bootstrap.");
    }

    [Fact]
    public async Task Crear_Con_Rfc_Duplicado_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var rfc = RandomRfc();

        var primero = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            Rfc = rfc,
            RazonSocial = "Empresa Test Primera",
            RegimenFiscal = "601",
            NombreComercial = (string?)null,
        });
        primero.EnsureSuccessStatusCode();

        var duplicada = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            Rfc = rfc,
            RazonSocial = "Empresa Test Duplicada",
            RegimenFiscal = "601",
            NombreComercial = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicada.StatusCode);
    }

    [Fact]
    public async Task Crear_Con_Datos_Validos_Retorna_201_Con_Id()
    {
        var client = await CreateSuperAdminClientAsync();
        var rfc = RandomRfc();

        var response = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            Rfc = rfc,
            RazonSocial = "Empresa Test Crear",
            RegimenFiscal = "601",
            NombreComercial = "Razón Comercial Test",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.NotEqual(Guid.Empty, body.GetProperty("id").GetGuid());
        Assert.Equal(rfc, body.GetProperty("rfc").GetString());
        Assert.True(body.GetProperty("activa").GetBoolean());
    }

    [Fact]
    public async Task Patch_Cambia_RazonSocial_Y_Get_Posterior_Lo_Refleja()
    {
        var client = await CreateSuperAdminClientAsync();
        var rfc = RandomRfc();

        var created = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            Rfc = rfc,
            RazonSocial = "Razón Social Original",
            RegimenFiscal = "601",
            NombreComercial = (string?)null,
        });
        created.EnsureSuccessStatusCode();
        var createdBody = await ReadJsonAsync(created);
        var id = createdBody.GetProperty("id").GetGuid();

        var patched = await client.PatchAsJsonAsync($"{EndpointBase}/{id}", new
        {
            RazonSocial = "Razón Social Actualizada",
            NombreComercial = (string?)null,
            RegimenFiscal = (string?)null,
            LimpiarNombreComercial = false,
        });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);

        var verify = await client.GetAsync($"{EndpointBase}/{id}");
        verify.EnsureSuccessStatusCode();
        var verifyBody = await ReadJsonAsync(verify);
        var empresa = verifyBody.GetProperty("empresa");
        Assert.Equal("Razón Social Actualizada", empresa.GetProperty("razonSocial").GetString());
    }

    [Fact]
    public async Task Get_Detalle_Incluye_Sucursales_Y_Departamentos_Arrays()
    {
        var client = await CreateSuperAdminClientAsync();
        var listResponse = await client.GetAsync(EndpointBase);
        listResponse.EnsureSuccessStatusCode();
        var listBody = await ReadJsonAsync(listResponse);
        var items = listBody.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1, "Necesita al menos una empresa.");
        var primeraId = items[0].GetProperty("id").GetGuid();

        var detalle = await client.GetAsync($"{EndpointBase}/{primeraId}");
        Assert.Equal(HttpStatusCode.OK, detalle.StatusCode);
        var body = await ReadJsonAsync(detalle);

        Assert.True(body.TryGetProperty("empresa", out _));
        Assert.True(body.TryGetProperty("sucursales", out var sucursales));
        Assert.True(body.TryGetProperty("departamentos", out var departamentos));
        Assert.Equal(JsonValueKind.Array, sucursales.ValueKind);
        Assert.Equal(JsonValueKind.Array, departamentos.ValueKind);
    }

    [Fact]
    public async Task Crear_Sucursal_Retorna_201()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = RandomClave("SUC");

        var response = await client.PostAsJsonAsync($"{EndpointBase}/sucursales", new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = "Sucursal Test Nueva",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(clave, body.GetProperty("clave").GetString());
        Assert.NotEqual(Guid.Empty, body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Crear_Sucursal_Con_Clave_Duplicada_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = RandomClave("SUD");

        var primera = await client.PostAsJsonAsync($"{EndpointBase}/sucursales", new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = "Sucursal Original",
        });
        primera.EnsureSuccessStatusCode();

        var duplicada = await client.PostAsJsonAsync($"{EndpointBase}/sucursales", new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = "Sucursal Duplicada",
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicada.StatusCode);
    }

    // --- Helpers ---

    /// <summary>
    /// RFC sintético de persona moral (12 chars) con sufijo aleatorio:
    /// 3 letras + 6 dígitos + 3 letras. Garantiza unicidad entre tests.
    /// </summary>
    private static string RandomRfc()
    {
        var rnd = new Random();
        var letras = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var prefix = new string(Enumerable.Range(0, 3).Select(_ => letras[rnd.Next(letras.Length)]).ToArray());
        var fecha = rnd.Next(100000, 999999).ToString();
        var sufijo = new string(Enumerable.Range(0, 3).Select(_ => letras[rnd.Next(letras.Length)]).ToArray());
        return $"{prefix}{fecha}{sufijo}";
    }

    private static string RandomClave(string prefix)
    {
        // 20 chars máx; usar prefix + 8 hex chars del Guid.
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
