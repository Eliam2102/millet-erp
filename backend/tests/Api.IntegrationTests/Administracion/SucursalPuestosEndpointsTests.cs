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
/// Tests integration del CRUD de asignaciones Sucursal ↔ Puesto
/// (F1-ADM-01 Fase 2). Análogo exacto de
/// <see cref="SucursalDepartamentosEndpointsTests"/>. Endpoint:
/// <c>/api/v1/admin/empresas/sucursales/{sucursalId}/puestos</c>.
/// </summary>
public class SucursalPuestosEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms-sxp";
    private const string EmpresasBase = "/api/v1/admin/empresas";

    private readonly WebApplicationFactory<Program> _factory;

    public SucursalPuestosEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Asignar_Con_Datos_Validos_Retorna_201()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var puestoId = await CrearPuestoAsync(client);

        var response = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(sucursalId, body.GetProperty("sucursalId").GetGuid());
        Assert.Equal(puestoId, body.GetProperty("puestoId").GetGuid());
        Assert.Equal(0, body.GetProperty("estatus").GetInt32()); // EstatusCatalogo.Activo
    }

    [Fact]
    public async Task Asignar_Duplicado_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var puestoId = await CrearPuestoAsync(client);

        var primero = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            content: null);
        primero.EnsureSuccessStatusCode();

        var duplicado = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            content: null);

        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);
    }

    [Fact]
    public async Task Asignar_Puesto_De_OtraEmpresa_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var otraEmpresa = await client.PostAsJsonAsync(EmpresasBase, new
        {
            Id = Guid.Empty,
            Rfc = ("TST" + Guid.NewGuid().ToString("N"))[..13].ToUpperInvariant(),
            RazonSocial = "Empresa ficticia para puesto ajeno",
            RegimenFiscal = "601",
        });
        otraEmpresa.EnsureSuccessStatusCode();
        var otraEmpresaId = (await ReadJsonAsync(otraEmpresa)).GetProperty("id").GetGuid();
        Guid puestoId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
            using var bypass = empresaContext.Bypass();
            puestoId = Guid.CreateVersion7();
            db.Puestos.Add(new Puesto(
                puestoId,
                otraEmpresaId,
                RandomClave("OTRAP"),
                "Puesto de otra empresa"));
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("RELACION_INVALIDA", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Asignar_A_Sucursal_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var puestoId = await CrearPuestoAsync(client);

        var response = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/puestos/{puestoId}",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Asignar_Puesto_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);

        var response = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{Guid.NewGuid()}",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Listar_Retorna_200_Con_Asignaciones()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var puestoId = await CrearPuestoAsync(client);
        await AsignarAsync(client, sucursalId, puestoId);

        var response = await client.GetAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos");

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
            $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/puestos");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Listar_No_Filtra_Fuga_De_Datos_Entre_Sucursales()
    {
        // Aislamiento cross-sucursal: el listado de la sucursal A no debe
        // incluir puestos asignados solo a la sucursal B, aunque ambas
        // pertenezcan a la misma empresa (F1-ADM-01 Fase 4).
        var client = await CreateSuperAdminClientAsync();
        var sucursalA = await CrearSucursalAsync(client, "SXP-A");
        var sucursalB = await CrearSucursalAsync(client, "SXP-B");
        var puestoA = await CrearPuestoAsync(client, "PXS-A");
        var puestoB = await CrearPuestoAsync(client, "PXS-B");
        await AsignarAsync(client, sucursalA, puestoA);
        await AsignarAsync(client, sucursalB, puestoB);

        var respuestaA = await client.GetAsync($"{EmpresasBase}/sucursales/{sucursalA}/puestos");
        var respuestaB = await client.GetAsync($"{EmpresasBase}/sucursales/{sucursalB}/puestos");

        Assert.Equal(HttpStatusCode.OK, respuestaA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, respuestaB.StatusCode);
        var bodyA = await ReadJsonAsync(respuestaA);
        var bodyB = await ReadJsonAsync(respuestaB);

        var idsA = bodyA.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("puestoId").GetGuid())
            .ToList();
        var idsB = bodyB.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("puestoId").GetGuid())
            .ToList();

        Assert.Contains(puestoA, idsA);
        Assert.DoesNotContain(puestoB, idsA);
        Assert.Contains(puestoB, idsB);
        Assert.DoesNotContain(puestoA, idsB);
    }

    [Fact]
    public async Task Desactivar_Retorna_200_Con_Estatus_Inactivo()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var puestoId = await CrearPuestoAsync(client);
        await AsignarAsync(client, sucursalId, puestoId);

        var response = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}/desactivar",
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
        var puestoId = await CrearPuestoAsync(client);
        await AsignarAsync(client, sucursalId, puestoId);

        var desactivada = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}/desactivar",
            content: null);
        desactivada.EnsureSuccessStatusCode();

        var reactivada = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}/reactivar",
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
            $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/puestos");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Listar_Sin_Permiso_Retorna_403()
    {
        var client = await CreateNoPermsClientAsync();
        var response = await client.GetAsync(
            $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/puestos");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("")]            // asignar
    [InlineData("/desactivar")]
    [InlineData("/reactivar")]
    public async Task Mutaciones_Sin_Permiso_Retornan_403(string sufijo)
    {
        var client = await CreateNoPermsClientAsync();
        var url = $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/puestos/{Guid.NewGuid()}{sufijo}";
        var response = await client.PostAsync(url, content: null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("")]            // asignar
    [InlineData("/desactivar")]
    [InlineData("/reactivar")]
    public async Task Mutaciones_Sin_Token_Retornan_401(string sufijo)
    {
        var client = _factory.CreateClientWithIdempotency();
        var url = $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/puestos/{Guid.NewGuid()}{sufijo}";
        var response = await client.PostAsync(url, content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Helpers ---

    private async Task<HttpClient> CreateNoPermsClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(
            client, SinPermisosOid, "noperms-sxp@test.local", "Sin Permisos SXP");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string RandomClave(string prefix)
    {
        var hex = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        return $"{prefix}-{hex}";
    }

    private static async Task<Guid> CrearSucursalAsync(HttpClient client, string prefix = "SXP")
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

    private static async Task<Guid> CrearPuestoAsync(HttpClient client, string prefix = "PXS")
    {
        var clave = RandomClave(prefix);
        var response = await client.PostAsJsonAsync("/api/v1/admin/puestos", new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = $"Puesto {clave}",
        });
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        return body.GetProperty("id").GetGuid();
    }

    private static async Task AsignarAsync(HttpClient client, Guid sucursalId, Guid puestoId)
    {
        var response = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
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
