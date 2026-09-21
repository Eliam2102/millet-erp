using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Tests integration del CRUD de asignaciones Sucursal ↔ Departamento
/// (PR-A1). Endpoint:
/// <c>/api/v1/admin/empresas/sucursales/{sucursalId}/departamentos</c>.
///
/// <para>
/// Cada test crea una sucursal + departamento frescos con claves
/// aleatorias para aislamiento; los seeds dev (combinatoria completa
/// MID/MTY/QRO × COMPRAS/ALMACEN/...) conviven sin interferir.
/// </para>
/// </summary>
public class SucursalDepartamentosEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms-sxd";
    private const string EmpresasBase = "/api/v1/admin/empresas";

    private readonly WebApplicationFactory<Program> _factory;

    public SucursalDepartamentosEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Asignar_Con_Datos_Validos_Retorna_201()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var deptoId = await CrearDepartamentoAsync(client);

        var response = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/departamentos/{deptoId}",
            content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(sucursalId, body.GetProperty("sucursalId").GetGuid());
        Assert.Equal(deptoId, body.GetProperty("departamentoId").GetGuid());
        Assert.Equal(0, body.GetProperty("estatus").GetInt32()); // EstatusCatalogo.Activo
    }

    [Fact]
    public async Task Asignar_Duplicado_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var deptoId = await CrearDepartamentoAsync(client);

        var primero = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/departamentos/{deptoId}",
            content: null);
        primero.EnsureSuccessStatusCode();

        var duplicado = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/departamentos/{deptoId}",
            content: null);

        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);
    }

    [Fact]
    public async Task Asignar_Departamento_De_OtraEmpresa_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        Guid departamentoId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
            using var bypass = empresaContext.Bypass();
            var sucursal = await db.Sucursales
                .AsNoTracking()
                .SingleAsync(s => s.Id == sucursalId);
            var otraEmpresaId = await db.Empresas
                .Where(empresa => empresa.Id != sucursal.EmpresaId)
                .Select(empresa => empresa.Id)
                .FirstAsync();

            departamentoId = Guid.CreateVersion7();
            db.Departamentos.Add(new Departamento(
                departamentoId,
                otraEmpresaId,
                RandomClave("OTRA"),
                "Departamento de otra empresa"));
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/departamentos/{departamentoId}",
            content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("RELACION_INVALIDA", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Asignar_A_Sucursal_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var deptoId = await CrearDepartamentoAsync(client);

        var response = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/departamentos/{deptoId}",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Asignar_Departamento_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);

        var response = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/departamentos/{Guid.NewGuid()}",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Listar_Retorna_200_Con_Asignaciones()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var deptoId = await CrearDepartamentoAsync(client);
        await AsignarAsync(client, sucursalId, deptoId);

        var response = await client.GetAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/departamentos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.GetProperty("total").GetInt32() >= 1);
        var items = body.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.True(items.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Listar_De_Sucursal_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync(
            $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/departamentos");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Desactivar_Retorna_200_Con_Estatus_Inactivo()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var deptoId = await CrearDepartamentoAsync(client);
        await AsignarAsync(client, sucursalId, deptoId);

        var response = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/departamentos/{deptoId}/desactivar",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(1, body.GetProperty("estatus").GetInt32()); // EstatusCatalogo.Inactivo
    }

    [Fact]
    public async Task Reactivar_Despues_De_Desactivar_Retorna_200_Activo()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var deptoId = await CrearDepartamentoAsync(client);
        await AsignarAsync(client, sucursalId, deptoId);

        var desactivada = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/departamentos/{deptoId}/desactivar",
            content: null);
        desactivada.EnsureSuccessStatusCode();

        var reactivada = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/departamentos/{deptoId}/reactivar",
            content: null);

        Assert.Equal(HttpStatusCode.OK, reactivada.StatusCode);
        var body = await ReadJsonAsync(reactivada);
        Assert.Equal(0, body.GetProperty("estatus").GetInt32()); // EstatusCatalogo.Activo
    }

    [Fact]
    public async Task Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/departamentos");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Auth por-ruta (split D1): no-bypass conductual ---

    [Fact]
    public async Task Listar_Sin_Permiso_Retorna_403()
    {
        // Usuario autenticado SIN compartido.catalogos.leer → 403 en el GET.
        var client = await CreateNoPermsClientAsync();
        var response = await client.GetAsync(
            $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/departamentos");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("")]            // asignar
    [InlineData("/desactivar")]
    [InlineData("/reactivar")]
    public async Task Mutaciones_Sin_Permiso_Retornan_403(string sufijo)
    {
        // Usuario autenticado SIN …departamentos-gestionar → 403 en cada POST.
        var client = await CreateNoPermsClientAsync();
        var url = $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/departamentos/{Guid.NewGuid()}{sufijo}";
        var response = await client.PostAsync(url, content: null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("")]            // asignar
    [InlineData("/desactivar")]
    [InlineData("/reactivar")]
    public async Task Mutaciones_Sin_Token_Retornan_401(string sufijo)
    {
        // Con Idempotency-Key (para que el 401 no lo enmascare un 400 de key
        // faltante) pero sin token → 401 en cada POST.
        var client = _factory.CreateClientWithIdempotency();
        var url = $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/departamentos/{Guid.NewGuid()}{sufijo}";
        var response = await client.PostAsync(url, content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Helpers ---

    private async Task<HttpClient> CreateNoPermsClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(
            client, SinPermisosOid, "noperms-sxd@test.local", "Sin Permisos SXD");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string RandomClave(string prefix)
    {
        var hex = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        return $"{prefix}-{hex}";
    }

    private static async Task<Guid> CrearSucursalAsync(HttpClient client, string prefix = "SXD")
    {
        var clave = RandomClave(prefix);
        var response = await client.PostAsJsonAsync($"{EmpresasBase}/sucursales", new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = $"Sucursal {clave}",
        });
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CrearDepartamentoAsync(HttpClient client, string prefix = "DXS")
    {
        var clave = RandomClave(prefix);
        var response = await client.PostAsJsonAsync("/api/v1/admin/departamentos", new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = $"Depto {clave}",
        });
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        return body.GetProperty("id").GetGuid();
    }

    private static async Task AsignarAsync(HttpClient client, Guid sucursalId, Guid deptoId)
    {
        var response = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/departamentos/{deptoId}",
            content: null);
        response.EnsureSuccessStatusCode();
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
