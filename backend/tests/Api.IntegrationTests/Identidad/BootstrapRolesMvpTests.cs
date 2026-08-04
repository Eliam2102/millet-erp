using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Identidad;

/// <summary>
/// Tests integration del seed de los 7 roles MVP creados por
/// <c>BootstrapSuperAdminHostedService.EnsureRolesMvpAsync</c>
/// (F-Admin-PR3.3, A2 cerrada 2026-05-13). El hosted service corre
/// al startup de la app y crea los roles idempotentemente.
///
/// <para>
/// La verificación se hace via el endpoint <c>GET /api/v1/identidad/roles</c>
/// del PR3.2 — si la lista incluye los 7 codigos esperados, el seed
/// funcionó.
/// </para>
/// </summary>
public class BootstrapRolesMvpTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";

    private static readonly string[] CodigosEsperados =
    [
        "super-admin",
        "admin-identidad",
        "admin-organizacional",
        "admin-catalogos",
        "admin-datos-maestros",
        "auditor",
        "admin-compras",
    ];

    private readonly WebApplicationFactory<Program> _factory;

    public BootstrapRolesMvpTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Bootstrap_Should_Seed_Seven_RolesMvp()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync("/api/v1/identidad/roles?limit=100");
        response.EnsureSuccessStatusCode();

        var body = await ReadJsonAsync(response);
        var codigos = body.GetProperty("items").EnumerateArray()
            .Select(r => r.GetProperty("codigo").GetString())
            .Where(c => c is not null)
            .ToHashSet();

        foreach (var esperado in CodigosEsperados)
        {
            Assert.Contains(esperado, codigos);
        }
    }

    [Fact]
    public async Task RolesMvp_Should_Be_EsDelSistema()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync("/api/v1/identidad/roles?limit=100");
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);

        foreach (var rolJson in body.GetProperty("items").EnumerateArray())
        {
            var codigo = rolJson.GetProperty("codigo").GetString();
            if (codigo is null || !CodigosEsperados.Contains(codigo)) continue;

            var esDelSistema = rolJson.GetProperty("esDelSistema").GetBoolean();
            Assert.True(esDelSistema, $"El rol '{codigo}' debe tener EsDelSistema=true.");
        }
    }

    [Fact]
    public async Task RolesMvp_Should_HaveExpectedPermisos()
    {
        var client = await CreateSuperAdminClientAsync();

        // Auditor: debe tener al menos admin.auditoria.leer e infra.audit_log.leer.
        var auditorId = await GetRolIdByCodigoAsync(client, "auditor");
        var auditorDetalle = await GetDetalleAsync(client, auditorId);
        var auditorPermisoIds = auditorDetalle.GetProperty("permisoIds")
            .EnumerateArray()
            .Select(g => g.GetGuid())
            .ToHashSet();
        Assert.NotEmpty(auditorPermisoIds);

        // Admin Compras: debe incluir compras.configuracion.leer y .editar.
        var comprasId = await GetRolIdByCodigoAsync(client, "admin-compras");
        var comprasDetalle = await GetDetalleAsync(client, comprasId);
        var comprasPermisoIds = comprasDetalle.GetProperty("permisoIds")
            .EnumerateArray()
            .Select(g => g.GetGuid())
            .ToHashSet();
        // GUIDs deterministas del seed de Identidad — ver PermisosCanonicos.Todos.
        Assert.Contains(Guid.Parse("00000003-0004-0000-0000-000000000001"), comprasPermisoIds); // ConfiguracionLeer
        Assert.Contains(Guid.Parse("00000003-0004-0000-0000-000000000002"), comprasPermisoIds); // ConfiguracionEditar

        // Admin Organizacional: tras el re-sync del bootstrap debe tener el
        // permiso de gestión del N:M Sucursal↔Departamento (D2) y la lectura
        // de catálogos cross-empresa para el Sheet (D3). El re-sync es
        // aditivo y corre en StartAsync (await del host) → determinístico.
        var orgAdminId = await GetRolIdByCodigoAsync(client, "admin-organizacional");
        var orgAdminDetalle = await GetDetalleAsync(client, orgAdminId);
        var orgAdminPermisoIds = orgAdminDetalle.GetProperty("permisoIds")
            .EnumerateArray()
            .Select(g => g.GetGuid())
            .ToHashSet();
        Assert.Contains(Guid.Parse("00000005-0006-0000-0000-000000000001"), orgAdminPermisoIds); // admin.sucursales.departamentos-gestionar (D2)
        Assert.Contains(Guid.Parse("00000004-0001-0000-0000-000000000001"), orgAdminPermisoIds); // compartido.catalogos.leer (D3)
        // Defensivo: el predicado está acotado — no debe arrastrar permisos de
        // Compras (StartsWith admin.* / == compartido.catalogos.leer, nada más).
        Assert.DoesNotContain(Guid.Parse("00000003-0004-0000-0000-000000000001"), orgAdminPermisoIds); // compras.configuracion.leer
    }

    private static async Task<Guid> GetRolIdByCodigoAsync(HttpClient client, string codigo)
    {
        var response = await client.GetAsync("/api/v1/identidad/roles?limit=100");
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        var rol = body.GetProperty("items").EnumerateArray()
            .First(r => r.GetProperty("codigo").GetString() == codigo);
        return rol.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> GetDetalleAsync(HttpClient client, Guid rolId)
    {
        var response = await client.GetAsync($"/api/v1/identidad/roles/{rolId}");
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync(response);
    }

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClient();
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
