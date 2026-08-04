using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.IntegrationTests.Catalogos;

/// <summary>
/// Tests integration de CRUD completo de proveedores y artículos
/// (B.5). Cubre los 6 endpoints (POST/PATCH/DELETE × 2 entidades) con
/// happy paths + 401/403/404/422.
/// </summary>
public class CrudCatalogosEndpointsTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms";
    private static readonly Guid MonedaMxnId = Guid.Parse("00000001-0000-0000-0000-000000000001");

    private readonly StubsWebApplicationFactory _factory;

    public CrudCatalogosEndpointsTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // --- POST /proveedores ---

    [Fact]
    public async Task CrearProveedor_Sin_Permiso_Retorna_403()
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CrearProveedor_HappyPath_Retorna_201()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = $"PROV-B5-{Guid.NewGuid().ToString("N")[..8]}";

        var response = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody(clave));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal(clave, json.GetProperty("clave").GetString());
        Assert.NotEqual(Guid.Empty, json.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task CrearProveedor_Clave_Duplicada_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = $"PROV-DUP-{Guid.NewGuid().ToString("N")[..8]}";

        var first = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody(clave));
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody(clave));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        var json = await ReadJsonAsync(second);
        Assert.Equal("PROVEEDOR_CLAVE_DUPLICADA", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CrearProveedor_Moneda_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var body = BuildProveedorBody() with { MonedaPreferidaId = Guid.CreateVersion7() };

        var response = await client.PostAsJsonAsync("/api/v1/catalogos/proveedores", body);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("MONEDA_NO_ENCONTRADA", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CrearProveedor_Sin_IdempotencyKey_Retorna_400()
    {
        var rawClient = _factory.CreateClient();
        var token = await FakeLoginAsync(rawClient, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        rawClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await rawClient.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("MISSING_IDEMPOTENCY_KEY", json.GetProperty("code").GetString());
    }

    // --- PATCH /proveedores/{id} ---

    [Fact]
    public async Task ActualizarProveedor_HappyPath_Cambia_Campos()
    {
        var client = await CreateSuperAdminClientAsync();
        var crearResp = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody());
        var id = (await ReadJsonAsync(crearResp)).GetProperty("id").GetGuid();

        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/catalogos/proveedores/{id}",
            new
            {
                RazonSocial = "Razón Social Editada SA de CV",
                CondicionesPagoDias = (short)45,
                Email = "nuevo@editado.com",
            });
        Assert.Equal(HttpStatusCode.NoContent, patchResp.StatusCode);

        var detalle = await client.GetAsync($"/api/v1/catalogos/proveedores/{id}");
        var json = await ReadJsonAsync(detalle);
        Assert.Equal("Razón Social Editada SA de CV", json.GetProperty("razonSocial").GetString());
        Assert.Equal((short)45, json.GetProperty("condicionesPagoDias").GetInt16());
        Assert.Equal("nuevo@editado.com", json.GetProperty("email").GetString());
    }

    [Fact]
    public async Task ActualizarProveedor_Limpiar_Email_Setea_Null()
    {
        var client = await CreateSuperAdminClientAsync();
        var body = BuildProveedorBody() with { Email = "tieneemail@test.com" };
        var crearResp = await client.PostAsJsonAsync("/api/v1/catalogos/proveedores", body);
        var id = (await ReadJsonAsync(crearResp)).GetProperty("id").GetGuid();

        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/catalogos/proveedores/{id}",
            new { LimpiarEmail = true });
        patchResp.EnsureSuccessStatusCode();

        var detalle = await client.GetAsync($"/api/v1/catalogos/proveedores/{id}");
        var json = await ReadJsonAsync(detalle);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("email").ValueKind);
    }

    [Fact]
    public async Task ActualizarProveedor_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/catalogos/proveedores/{Guid.NewGuid()}",
            new { RazonSocial = "X" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- DELETE /proveedores/{id} ---

    [Fact]
    public async Task DesactivarProveedor_Cambia_Estatus_A_Inactivo()
    {
        var client = await CreateSuperAdminClientAsync();
        var crearResp = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody());
        var id = (await ReadJsonAsync(crearResp)).GetProperty("id").GetGuid();

        var deleteResp = await client.DeleteAsync($"/api/v1/catalogos/proveedores/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var detalle = await client.GetAsync($"/api/v1/catalogos/proveedores/{id}");
        var json = await ReadJsonAsync(detalle);
        // EstatusCatalogo.Inactivo = 1
        Assert.Equal((int)EstatusCatalogo.Inactivo, json.GetProperty("estatus").GetInt32());
    }

    [Fact]
    public async Task DesactivarProveedor_Idempotente_Segunda_Vez_Sigue_204()
    {
        var client = await CreateSuperAdminClientAsync();
        var crearResp = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody());
        var id = (await ReadJsonAsync(crearResp)).GetProperty("id").GetGuid();

        var first = await client.DeleteAsync($"/api/v1/catalogos/proveedores/{id}");
        var second = await client.DeleteAsync($"/api/v1/catalogos/proveedores/{id}");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }

    // --- POST /articulos ---

    [Fact]
    public async Task CrearArticulo_HappyPath_Retorna_201()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = $"ART-B5-{Guid.NewGuid().ToString("N")[..8]}";

        var response = await client.PostAsJsonAsync(
            "/api/v1/catalogos/articulos", BuildArticuloBody(clave));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CrearArticulo_Moneda_No_Registrada_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var body = BuildArticuloBody() with { PrecioReferenciaMonto = 10m, PrecioReferenciaMoneda = "ZZZ" };

        var response = await client.PostAsJsonAsync("/api/v1/catalogos/articulos", body);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("ARTICULO_MONEDA_NO_REGISTRADA", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CrearArticulo_Clave_Duplicada_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = $"ART-DUP-{Guid.NewGuid().ToString("N")[..8]}";

        await client.PostAsJsonAsync("/api/v1/catalogos/articulos", BuildArticuloBody(clave));
        var second = await client.PostAsJsonAsync("/api/v1/catalogos/articulos", BuildArticuloBody(clave));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        var json = await ReadJsonAsync(second);
        Assert.Equal("ARTICULO_CLAVE_DUPLICADA", json.GetProperty("code").GetString());
    }

    // --- PATCH /articulos/{id} ---

    [Fact]
    public async Task ActualizarArticulo_Cambio_Naturaleza_Individual_Refleja_En_Detalle()
    {
        var client = await CreateSuperAdminClientAsync();
        var crearResp = await client.PostAsJsonAsync(
            "/api/v1/catalogos/articulos", BuildArticuloBody());
        var id = (await ReadJsonAsync(crearResp)).GetProperty("id").GetGuid();

        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/catalogos/articulos/{id}",
            new { Naturaleza = (int)Naturaleza.Critico });
        patchResp.EnsureSuccessStatusCode();

        var detalle = await client.GetAsync($"/api/v1/catalogos/articulos/{id}");
        var json = await ReadJsonAsync(detalle);
        Assert.Equal((int)Naturaleza.Critico, json.GetProperty("naturaleza").GetInt32());

        // Paridad post-unificación del DTO: un solo record ArticuloDetalle es
        // compartido por /catalogos y /datos-maestros (fix Bug A). El detalle de
        // /catalogos debe seguir exponiendo unidadMedidaId; el artículo se creó
        // con UnidadPzaId y el PATCH solo tocó naturaleza, así que sigue siendo PZA.
        Assert.Equal(UnidadPzaId, json.GetProperty("unidadMedidaId").GetGuid());
    }

    // --- DELETE /articulos/{id} ---

    [Fact]
    public async Task DesactivarArticulo_Cambia_Estatus_A_Inactivo()
    {
        var client = await CreateSuperAdminClientAsync();
        var crearResp = await client.PostAsJsonAsync(
            "/api/v1/catalogos/articulos", BuildArticuloBody());
        var id = (await ReadJsonAsync(crearResp)).GetProperty("id").GetGuid();

        var deleteResp = await client.DeleteAsync($"/api/v1/catalogos/articulos/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var detalle = await client.GetAsync($"/api/v1/catalogos/articulos/{id}");
        var json = await ReadJsonAsync(detalle);
        Assert.Equal((int)EstatusCatalogo.Inactivo, json.GetProperty("estatus").GetInt32());
    }

    // --- Helpers ---

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static ProveedorBody BuildProveedorBody(string? clave = null) => new(
        Clave: clave ?? $"PROV-B5-{Guid.NewGuid().ToString("N")[..8]}",
        RazonSocial: "Test Proveedor SA de CV",
        Rfc: "TPR010101AAA",
        TipoPersona: TipoPersonaProveedor.Moral,
        NombreComercial: null,
        CondicionesPagoDias: (short)30,
        MonedaPreferidaId: MonedaMxnId,
        Email: null,
        Telefono: null);

    // Seed de unidades (ADR-0046): PZA = Conteo base.
    private static readonly Guid UnidadPzaId =
        Guid.Parse("00000002-0007-0000-0000-000000000001");

    private static ArticuloBody BuildArticuloBody(string? clave = null) => new(
        Clave: clave ?? $"ART-B5-{Guid.NewGuid().ToString("N")[..8]}",
        Nombre: "Artículo de prueba",
        UnidadMedidaId: UnidadPzaId,
        Naturaleza: Naturaleza.Estandar,
        DescripcionLarga: null,
        Categoria: "Test",
        PrecioReferenciaMonto: null,
        PrecioReferenciaMoneda: null);

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

    private sealed record ProveedorBody(
        string Clave,
        string RazonSocial,
        string Rfc,
        TipoPersonaProveedor TipoPersona,
        string? NombreComercial,
        short? CondicionesPagoDias,
        Guid? MonedaPreferidaId,
        string? Email,
        string? Telefono);

    private sealed record ArticuloBody(
        string Clave,
        string Nombre,
        Guid UnidadMedidaId,
        Naturaleza Naturaleza,
        string? DescripcionLarga,
        string? Categoria,
        decimal? PrecioReferenciaMonto,
        string? PrecioReferenciaMoneda);
}
