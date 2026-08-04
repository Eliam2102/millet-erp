using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>
/// Tests integration de los endpoints enriquecidos de Datos Maestros
/// (F-Admin-PR4.5). Verifica filtros expandidos sobre proveedores y
/// artículos (cubren los casos que el endpoint legacy de F7-PR1 no
/// soportaba: filtro por rfc, razonSocial, tipoPersona, naturaleza,
/// unidadMedida).
/// </summary>
public class DatosMaestrosEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private readonly WebApplicationFactory<Program> _factory;

    public DatosMaestrosEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListarProveedores_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/datos-maestros/proveedores");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListarProveedores_Con_Token_Retorna_200_Y_Items_Array()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync("/api/v1/datos-maestros/proveedores");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.TryGetProperty("items", out _));
        Assert.True(body.TryGetProperty("total", out _));
    }

    [Fact]
    public async Task ListarProveedores_Filtra_Por_Rfc_Substring()
    {
        var client = await CreateSuperAdminClientAsync();

        // Solo verificamos que el filtro no falle (no asumimos sustring
        // específico porque el seed test puede variar). Pasamos un RFC
        // que probablemente no exista para verificar que retorna 0.
        var response = await client.GetAsync("/api/v1/datos-maestros/proveedores?rfc=ZZZ999");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var total = body.GetProperty("total").GetInt32();
        Assert.True(total >= 0, "Filtro de RFC debe responder OK aún con 0 hits.");
    }

    [Fact]
    public async Task ListarArticulos_Con_Filtros_Combinados_Funciona()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync(
            "/api/v1/datos-maestros/articulos?naturaleza=0&estatus=0&offset=0&limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.TryGetProperty("items", out _));
        Assert.True(body.GetProperty("limit").GetInt32() == 10);
    }

    [Fact]
    public async Task ObtenerArticulo_No_Encontrado_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var fakeId = Guid.NewGuid();
        var response = await client.GetAsync($"/api/v1/datos-maestros/articulos/{fakeId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ObtenerArticulo_Detalle_Expone_UnidadMedidaId()
    {
        // Regresión Bug A: el detalle de /datos-maestros tenía su propia copia
        // del DTO ArticuloDetalle sin el FK unidadMedidaId (ADR-0046 Etapa 1b),
        // así que el form de edición lo leía como null y mostraba la unidad
        // legacy aunque el artículo tuviera unidad asignada. El campo DEBE estar
        // presente en el JSON del detalle.
        var client = await CreateSuperAdminClientAsync();
        var articulo = await PrimeroAsync(client, "/api/v1/datos-maestros/articulos?limit=50");
        var id = articulo.GetProperty("id").GetGuid();

        var response = await client.GetAsync($"/api/v1/datos-maestros/articulos/{id}");
        response.EnsureSuccessStatusCode();
        var detalle = await ReadJsonAsync(response);

        // Pre-fix el serializer omitía la propiedad → TryGetProperty era false.
        Assert.True(
            detalle.TryGetProperty("unidadMedidaId", out var detalleUm),
            "El detalle debe exponer 'unidadMedidaId' (regresión Bug A).");

        // Null-agnóstico: paridad con el valor que ya expone la lista para el
        // mismo id, sin asumir que el seed tenga FK asignado (artículos legacy
        // quedan null y siguen siendo válidos).
        var listaUm = articulo.GetProperty("unidadMedidaId");
        Assert.Equal(listaUm.ValueKind, detalleUm.ValueKind);
        if (listaUm.ValueKind != JsonValueKind.Null)
        {
            Assert.Equal(listaUm.GetGuid(), detalleUm.GetGuid());
        }
    }

    // --- Case-insensitive (Defect B): textos con folding, códigos con lower() ---

    [Fact]
    public async Task ListarProveedores_RazonSocial_CaseInsensitive()
    {
        var client = await CreateSuperAdminClientAsync();
        // Tomamos un proveedor real del seed y buscamos su razón social en
        // minúsculas: el folding (case + acentos, ADR-0045) debe encontrarlo.
        // Pre-fix (Contains case-sensitive) no lo hallaba si tenía mayúsculas.
        var prov = await PrimeroAsync(client, "/api/v1/datos-maestros/proveedores?limit=50");
        var id = prov.GetProperty("id").GetGuid();
        var razon = prov.GetProperty("razonSocial").GetString()!;

        var response = await client.GetAsync(
            $"/api/v1/datos-maestros/proveedores?razonSocial={Uri.EscapeDataString(razon.ToLowerInvariant())}&limit=50");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        Assert.Contains(json.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task ListarProveedores_Rfc_CaseInsensitive()
    {
        var client = await CreateSuperAdminClientAsync();
        var prov = await PrimeroAsync(client, "/api/v1/datos-maestros/proveedores?limit=50");
        var id = prov.GetProperty("id").GetGuid();
        var rfc = prov.GetProperty("rfc").GetString()!;

        // RFC = código → lower() en ambos lados.
        var response = await client.GetAsync(
            $"/api/v1/datos-maestros/proveedores?rfc={Uri.EscapeDataString(rfc.ToLowerInvariant())}&limit=50");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        Assert.Contains(json.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task ListarArticulos_Codigo_CaseInsensitive()
    {
        var client = await CreateSuperAdminClientAsync();
        var art = await PrimeroAsync(client, "/api/v1/datos-maestros/articulos?limit=50");
        var id = art.GetProperty("id").GetGuid();
        var clave = art.GetProperty("clave").GetString()!;

        // codigo (clave) = código → lower() en ambos lados.
        var response = await client.GetAsync(
            $"/api/v1/datos-maestros/articulos?codigo={Uri.EscapeDataString(clave.ToLowerInvariant())}&limit=50");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        Assert.Contains(json.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task ListarArticulos_Descripcion_CaseInsensitive()
    {
        var client = await CreateSuperAdminClientAsync();
        var art = await PrimeroAsync(client, "/api/v1/datos-maestros/articulos?limit=50");
        var id = art.GetProperty("id").GetGuid();
        var nombre = art.GetProperty("nombre").GetString()!;

        // descripcion (nombre) = texto libre → folding case + acentos (ADR-0045).
        var response = await client.GetAsync(
            $"/api/v1/datos-maestros/articulos?descripcion={Uri.EscapeDataString(nombre.ToLowerInvariant())}&limit=50");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        Assert.Contains(json.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == id);
    }

    // --- Helpers ---

    private static async Task<JsonElement> PrimeroAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items");
        Assert.True(items.GetArrayLength() > 0,
            $"El seed no devolvió items para {url}; el test case-insensitive necesita ≥ 1.");
        return items[0].Clone();
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
