using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Compras.IntegrationTests.Fixtures;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.IntegrationTests.Catalogos;

/// <summary>
/// Tests integration de los endpoints read-only de catálogos
/// compartidos (F7-PR1):
/// <list type="bullet">
///   <item>GET /api/v1/catalogos/proveedores (paginado + filtros).</item>
///   <item>GET /api/v1/catalogos/proveedores/{id}.</item>
///   <item>GET /api/v1/catalogos/articulos (paginado + filtros).</item>
///   <item>GET /api/v1/catalogos/articulos/{id}.</item>
/// </list>
///
/// <para>
/// El seed test data del <c>CatalogosTestSeedHostedService</c> alimenta
/// la BD con 5 proveedores y 10 artículos (incluyendo los 2 magic GUIDs
/// para stub de stock). Los tests asumen ese seed presente.
/// </para>
/// </summary>
public class CatalogosEndpointsTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms";
    private static readonly Guid ProveedorActivoId = Guid.Parse("00000005-0001-0000-0000-000000000001"); // PROV-EST
    private static readonly Guid ProveedorInactivoId = Guid.Parse("00000005-0001-0000-0000-000000000005"); // PROV-INA
    private static readonly Guid ProveedorMayoristaId = Guid.Parse("00000005-0001-0000-0000-000000000003"); // PROV-MAY (nombreComercial "DiMa Insumos")
    private static readonly Guid ArticuloEstandarId = Guid.Parse("00000005-0002-0000-0000-000000000001"); // ART-EST-01

    private readonly StubsWebApplicationFactory _factory;

    public CatalogosEndpointsTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // --- Proveedores ---

    [Fact]
    public async Task ListarProveedores_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/catalogos/proveedores");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListarProveedores_Sin_Permiso_Retorna_403()
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/catalogos/proveedores");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListarProveedores_ConPermiso_DevuelveSeed()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync("/api/v1/catalogos/proveedores?limit=200");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);

        var total = json.GetProperty("total").GetInt32();
        Assert.True(total >= 5, $"Esperaban ≥ 5 proveedores del seed; encontraron {total}.");
        var items = json.GetProperty("items");
        Assert.Contains(
            items.EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == ProveedorActivoId);
    }

    [Fact]
    public async Task ListarProveedores_FiltradoPor_Estatus_Inactivo()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync(
            $"/api/v1/catalogos/proveedores?estatus={(int)EstatusCatalogo.Inactivo}");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items");
        Assert.All(items.EnumerateArray(), i =>
            Assert.Equal((int)EstatusCatalogo.Inactivo, i.GetProperty("estatus").GetInt32()));
        Assert.Contains(items.EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == ProveedorInactivoId);
    }

    [Fact]
    public async Task ListarProveedores_FiltradoPor_Clave_CaseInsensitive()
    {
        var client = await CreateSuperAdminClientAsync();

        // El seed tiene la clave "PROV-EST" (mayúsculas). Buscar "prov-est"
        // (minúsculas) debe encontrarlo: lower() en ambos lados (Defect B).
        // Antes del fix (Contains case-sensitive) esto devolvía 0.
        var response = await client.GetAsync("/api/v1/catalogos/proveedores?clave=prov-est&limit=50");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        Assert.Contains(
            json.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == ProveedorActivoId);
    }

    [Fact]
    public async Task ListarProveedores_FiltradoPor_Nombre_RazonSocial_InsensibleAcentosYMayusculas()
    {
        var client = await CreateSuperAdminClientAsync();

        // PROV-EST se llama "Suministros Estándar SA de CV" (acento + mayúsculas).
        // Buscar "suministros estandar" (minúsculas, sin acento) debe encontrarlo:
        // folding translate sobre razón social (ADR-0045, Defect A). Frase
        // distintiva del seed → no colisiona con los proveedores reales del import.
        await AssertNombreProveedorEncuentraAsync(client, "suministros estandar", ProveedorActivoId);

        // Mayúscula CON acento en la aguja: ejercita lower() sobre vocal acentuada.
        await AssertNombreProveedorEncuentraAsync(client, "SUMINISTROS ESTÁNDAR", ProveedorActivoId);
    }

    [Fact]
    public async Task ListarProveedores_FiltradoPor_Nombre_NombreComercial()
    {
        var client = await CreateSuperAdminClientAsync();

        // PROV-MAY: razón social "Distribuidora Mayorista de Insumos SA", nombre
        // comercial "DiMa Insumos". "dima insumos" NO está en la razón social →
        // encontrarlo prueba la rama OR sobre NombreComercial (con null-guard).
        await AssertNombreProveedorEncuentraAsync(client, "dima insumos", ProveedorMayoristaId);
    }

    [Fact]
    public async Task ListarProveedores_Clave_TienePrecedencia_SobreNombre()
    {
        var client = await CreateSuperAdminClientAsync();

        // clave y nombre son excluyentes; si llegan ambos gana clave. El nombre
        // es imposible → si se aplicara, 0 resultados; como gana clave, aparece
        // PROV-EST.
        var response = await client.GetAsync(
            "/api/v1/catalogos/proveedores?clave=PROV-EST&nombre=zzz-no-existe&limit=50");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        Assert.Contains(
            json.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == ProveedorActivoId);
    }

    [Fact]
    public async Task ObtenerProveedor_Existente_Retorna_200()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync($"/api/v1/catalogos/proveedores/{ProveedorActivoId}");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        Assert.Equal("PROV-EST", json.GetProperty("clave").GetString());
        Assert.Equal((int)EstatusCatalogo.Activo, json.GetProperty("estatus").GetInt32());
    }

    [Fact]
    public async Task ObtenerProveedor_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync($"/api/v1/catalogos/proveedores/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Articulos ---

    [Fact]
    public async Task ListarArticulos_ConPermiso_DevuelveSeed()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync("/api/v1/catalogos/articulos?limit=200");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        var total = json.GetProperty("total").GetInt32();
        Assert.True(total >= 10, $"Esperaban ≥ 10 artículos del seed; encontraron {total}.");
    }

    [Fact]
    public async Task ListarArticulos_FiltradoPor_Naturaleza_Critico()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync(
            $"/api/v1/catalogos/articulos?naturaleza={(int)Naturaleza.Critico}&limit=50");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items");
        Assert.All(items.EnumerateArray(), i =>
            Assert.Equal((int)Naturaleza.Critico, i.GetProperty("naturaleza").GetInt32()));
        Assert.True(items.GetArrayLength() >= 2, "Esperaban ≥ 2 artículos críticos del seed.");
    }

    [Fact]
    public async Task ListarArticulos_FiltradoPor_Clave_PrefijoART()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync("/api/v1/catalogos/articulos?clave=ART-EST&limit=50");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 2);
        Assert.All(items.EnumerateArray(), i =>
            Assert.Contains("ART-EST", i.GetProperty("clave").GetString()));
    }

    [Fact]
    public async Task ListarArticulos_FiltradoPor_Clave_CaseInsensitive()
    {
        var client = await CreateSuperAdminClientAsync();

        // Las claves del seed son "ART-EST-*" (mayúsculas). Buscar "art-est"
        // (minúsculas) debe traer los mismos: lower() en ambos lados (Defect B).
        var response = await client.GetAsync("/api/v1/catalogos/articulos?clave=art-est&limit=50");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 2);
        Assert.All(items.EnumerateArray(), i =>
            Assert.Contains("ART-EST", i.GetProperty("clave").GetString()));
    }

    [Fact]
    public async Task ListarArticulos_FiltradoPor_Nombre_InsensibleAcentosYMayusculas()
    {
        var client = await CreateSuperAdminClientAsync();

        // El seed ART-EST-01 se llama "Papelería de oficina genérica" (con
        // acentos y mayúscula). Buscar "papeleria" (minúscula, sin acento)
        // debe encontrarlo: folding con translate sobre ambos lados (ADR-0045).
        await AssertNombreEncuentraAsync(client, "papeleria", ArticuloEstandarId);

        // Match en cualquier posición + mayúsculas: "OFICINA" va en medio.
        await AssertNombreEncuentraAsync(client, "OFICINA", ArticuloEstandarId);

        // Mayúscula CON acento en la aguja: "PAPELERÍA" ejercita lower() sobre
        // una vocal acentuada mayúscula contra el COLLATION real de la BD. En
        // un locale C no plegaría (Í no baja a í) → este caso lo cacha. ADR-0045.
        await AssertNombreEncuentraAsync(client, "PAPELERÍA", ArticuloEstandarId);

        // Otro caso con acento (í→i): "quimicos" encuentra "Reactivos
        // químicos críticos" (ART-CRT-02).
        await AssertNombreEncuentraAsync(
            client, "quimicos", Guid.Parse("00000005-0002-0000-0000-000000000006"));
    }

    [Fact]
    public async Task ListarArticulos_ClaveVacia_NoGanaPrecedencia_AplicaNombre()
    {
        var client = await CreateSuperAdminClientAsync();

        // clave="" (string vacío en el query) NO debe ganar precedencia:
        // IsNullOrWhiteSpace lo descarta y se aplica el filtro por nombre. Si
        // ganara, Contains("") traería TODO ignorando el nombre (regresión).
        var response = await client.GetAsync(
            "/api/v1/catalogos/articulos?clave=&nombre=papeleria&limit=50");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        Assert.Contains(
            json.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == ArticuloEstandarId);
    }

    [Fact]
    public async Task ListarArticulos_Clave_TienePrecedencia_SobreNombre()
    {
        var client = await CreateSuperAdminClientAsync();

        // clave y nombre son excluyentes; si llegan ambos gana clave. El
        // nombre es imposible → si se aplicara, 0 resultados; como gana
        // clave, siguen apareciendo los ART-EST.
        var response = await client.GetAsync(
            "/api/v1/catalogos/articulos?clave=ART-EST&nombre=zzz-no-existe&limit=50");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 2);
        Assert.All(items.EnumerateArray(), i =>
            Assert.Contains("ART-EST", i.GetProperty("clave").GetString()));
    }

    private static async Task AssertNombreEncuentraAsync(
        HttpClient client, string nombre, Guid esperadoId)
    {
        var response = await client.GetAsync(
            $"/api/v1/catalogos/articulos?nombre={Uri.EscapeDataString(nombre)}&limit=50");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        Assert.Contains(
            json.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == esperadoId);
    }

    private static async Task AssertNombreProveedorEncuentraAsync(
        HttpClient client, string nombre, Guid esperadoId)
    {
        var response = await client.GetAsync(
            $"/api/v1/catalogos/proveedores?nombre={Uri.EscapeDataString(nombre)}&limit=50");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        Assert.Contains(
            json.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == esperadoId);
    }

    [Fact]
    public async Task ObtenerArticulo_Existente_Retorna_200()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync($"/api/v1/catalogos/articulos/{ArticuloEstandarId}");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        Assert.Equal("ART-EST-01", json.GetProperty("clave").GetString());
        Assert.Equal((int)Naturaleza.Estandar, json.GetProperty("naturaleza").GetInt32());
    }

    // --- Validación cross-table en AgregarLineaCommand (F7-PR1) ---

    [Fact]
    public async Task AgregarLinea_ConArticuloInexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearRequisicionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/lineas",
            new
            {
                ArticuloId = Guid.NewGuid(),
                Cantidad = 10m,
                UnidadMedida = "PZA",
                PrecioEstimadoMonto = 15m,
                PrecioEstimadoMoneda = "MXN",
                CuentaContableId = (Guid?)null,
                CentroCostoId = (Guid?)TestComprasFixtures.CentroCostoMaquinaSeed,
                Proyecto = (string?)null,
                FechaRequerida = (DateOnly?)null,
                Notas = (string?)null,
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("ARTICULO_NO_ENCONTRADO", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CrearRequisicion_ConProveedorInactivo_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/compras/requisiciones",
            TestComprasFixtures.BuildCrearRqValidBody(
                proveedorSugeridoId: ProveedorInactivoId,
                descripcion: "Test F7-PR1 proveedor inactivo"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("PROVEEDOR_INACTIVO", body.GetProperty("code").GetString());
    }

    // --- Helpers ---

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> CrearRequisicionAsync(HttpClient client)
    {
        var body = TestComprasFixtures.BuildCrearRqValidBody(descripcion: "Test F7-PR1");
        var response = await client.PostAsJsonAsync("/api/v1/compras/requisiciones", body);
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        return json.GetProperty("id").GetGuid();
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
        var json = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(json);
        return doc.RootElement.GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("fake-login no devolvió accessToken");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(json);
        return doc.RootElement.Clone();
    }
}
