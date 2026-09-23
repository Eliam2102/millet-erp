using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Tests integration del CRUD de Departamentos (F-Admin-PR2.3).
/// Endpoint: <c>/api/v1/admin/departamentos</c>.
/// </summary>
public class DepartamentosEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string EndpointBase = "/api/v1/admin/departamentos";

    private readonly WebApplicationFactory<Program> _factory;

    public DepartamentosEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Crear_Departamento_Con_Datos_Validos_Retorna_201()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = RandomClave("DEPT");

        var response = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = "Departamento Test Nuevo",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(clave, body.GetProperty("clave").GetString());
        Assert.NotEqual(Guid.Empty, body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Crear_Departamento_Con_Clave_Duplicada_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = RandomClave("DEPD");

        var primero = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = "Departamento Original",
        });
        primero.EnsureSuccessStatusCode();

        var duplicado = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = "Departamento Duplicado",
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);
    }

    [Fact]
    public async Task Listar_Desactivar_Reactivar_Departamento_Retorna_Estados_Correctos()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = RandomClave("DEPL");
        var creado = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = "Departamento ciclo completo",
        });
        creado.EnsureSuccessStatusCode();
        var creadoBody = await ReadJsonAsync(creado);
        var id = creadoBody.GetProperty("id").GetGuid();

        var listado = await client.GetAsync($"{EndpointBase}?q={clave}");
        Assert.Equal(HttpStatusCode.OK, listado.StatusCode);
        var listadoBody = await ReadJsonAsync(listado);
        Assert.Contains(
            listadoBody.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == id);

        var desactivado = await client.PostAsync($"{EndpointBase}/{id}/desactivar", content: null);
        Assert.Equal(HttpStatusCode.OK, desactivado.StatusCode);
        Assert.Equal(1, (await ReadJsonAsync(desactivado)).GetProperty("estatus").GetInt32());

        var reactivado = await client.PostAsync($"{EndpointBase}/{id}/reactivar", content: null);
        Assert.Equal(HttpStatusCode.OK, reactivado.StatusCode);
        Assert.Equal(0, (await ReadJsonAsync(reactivado)).GetProperty("estatus").GetInt32());
    }

    [Fact]
    public async Task Listar_Con_SucursalId_No_Filtra_Fuga_De_Datos_Entre_Sucursales()
    {
        // El filtro opcional ?sucursalId= (F1-ADM-01 Fase 2/4) sobre el
        // catálogo plano de departamentos debe restringir estrictamente
        // a los departamentos asignados a esa sucursal, no mezclar con
        // los de otra sucursal de la misma empresa.
        var client = await CreateSuperAdminClientAsync();
        var sucursalA = await CrearSucursalAsync(client, "DEPF-A");
        var sucursalB = await CrearSucursalAsync(client, "DEPF-B");
        var deptoA = await CrearDepartamentoAsync(client, "DEPF-DA");
        var deptoB = await CrearDepartamentoAsync(client, "DEPF-DB");
        await AsignarASucursalAsync(client, sucursalA, deptoA);
        await AsignarASucursalAsync(client, sucursalB, deptoB);

        var respuestaA = await client.GetAsync($"{EndpointBase}?sucursalId={sucursalA}");
        var respuestaB = await client.GetAsync($"{EndpointBase}?sucursalId={sucursalB}");

        Assert.Equal(HttpStatusCode.OK, respuestaA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, respuestaB.StatusCode);
        var bodyA = await ReadJsonAsync(respuestaA);
        var bodyB = await ReadJsonAsync(respuestaB);

        var idsA = bodyA.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid())
            .ToList();
        var idsB = bodyB.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid())
            .ToList();

        Assert.Contains(deptoA, idsA);
        Assert.DoesNotContain(deptoB, idsA);
        Assert.Contains(deptoB, idsB);
        Assert.DoesNotContain(deptoA, idsB);
    }

    [Fact]
    public async Task Crear_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            Clave = "ANY",
            Nombre = "x",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Helpers ---

    private static string RandomClave(string prefix)
    {
        var hex = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        return $"{prefix}-{hex}";
    }

    private static async Task<Guid> CrearSucursalAsync(HttpClient client, string prefix)
    {
        var clave = RandomClave(prefix);
        var response = await client.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = $"Sucursal {clave}",
        });
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CrearDepartamentoAsync(HttpClient client, string prefix)
    {
        var clave = RandomClave(prefix);
        var response = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = $"Depto {clave}",
        });
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        return body.GetProperty("id").GetGuid();
    }

    private static async Task AsignarASucursalAsync(HttpClient client, Guid sucursalId, Guid departamentoId)
    {
        var response = await client.PostAsync(
            $"/api/v1/admin/empresas/sucursales/{sucursalId}/departamentos/{departamentoId}",
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
