using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Tests integration del CRUD de Puestos (ADM-PR1). Endpoint:
/// <c>/api/v1/admin/puestos</c>. Análogo de
/// <see cref="DepartamentosEndpointsTests"/>; se agrega aquí (F1-ADM-01
/// Fase 4) el test de aislamiento cross-sucursal del filtro opcional
/// <c>?sucursalId=</c>, que hasta ahora no tenía cobertura explícita.
/// </summary>
public class PuestosEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string EndpointBase = "/api/v1/admin/puestos";

    private readonly WebApplicationFactory<Program> _factory;

    public PuestosEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Crear_Puesto_Con_Datos_Validos_Retorna_201()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = RandomClave("PST");

        var response = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = "Puesto Test Nuevo",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(clave, body.GetProperty("clave").GetString());
        Assert.NotEqual(Guid.Empty, body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Crear_Puesto_Con_RolSugerido_Valido_Retorna_201_Con_RolSugerido()
    {
        var client = await CreateSuperAdminClientAsync();
        var rolesResp = await client.GetAsync("/api/v1/identidad/roles");
        rolesResp.EnsureSuccessStatusCode();
        var rolesBody = await ReadJsonAsync(rolesResp);
        var primerRol = rolesBody.GetProperty("items").EnumerateArray().First();
        var rolId = primerRol.GetProperty("id").GetGuid();
        var rolNombre = primerRol.GetProperty("nombre").GetString();

        var clave = RandomClave("PST-R");
        var response = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = "Puesto Con Rol Sugerido",
            RolSugeridoId = rolId,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(rolId, body.GetProperty("rolSugeridoId").GetGuid());
        Assert.Equal(rolNombre, body.GetProperty("rolSugeridoNombre").GetString());
    }

    [Fact]
    public async Task Crear_Puesto_Con_RolSugerido_Inexistente_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var rolInexistente = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            Clave = RandomClave("PST-INV"),
            Nombre = "Puesto Invalido",
            RolSugeridoId = rolInexistente,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("ROL_SUGERIDO_INVALIDO", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Actualizar_Puesto_RolSugerido_Y_Limpiar_Funciona_Correctamente()
    {
        var client = await CreateSuperAdminClientAsync();
        var puestoId = await CrearPuestoAsync(client, "PST-ED");

        var rolesResp = await client.GetAsync("/api/v1/identidad/roles");
        var rolesBody = await ReadJsonAsync(rolesResp);
        var primerRol = rolesBody.GetProperty("items").EnumerateArray().First();
        var rolId = primerRol.GetProperty("id").GetGuid();
        var rolNombre = primerRol.GetProperty("nombre").GetString();

        // 1. Asignar rol
        var patchResp = await client.PatchAsJsonAsync($"{EndpointBase}/{puestoId}", new
        {
            Nombre = "Puesto Editado Con Rol",
            RolSugeridoId = rolId,
        });
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var patchBody = await ReadJsonAsync(patchResp);
        Assert.Equal(rolId, patchBody.GetProperty("rolSugeridoId").GetGuid());
        Assert.Equal(rolNombre, patchBody.GetProperty("rolSugeridoNombre").GetString());

        // 2. Limpiar rol
        var limpiarResp = await client.PatchAsJsonAsync($"{EndpointBase}/{puestoId}", new
        {
            Nombre = (string?)null,
            LimpiarRolSugerido = true,
        });
        Assert.Equal(HttpStatusCode.OK, limpiarResp.StatusCode);
        var limpiarBody = await ReadJsonAsync(limpiarResp);
        Assert.Equal(JsonValueKind.Null, limpiarBody.GetProperty("rolSugeridoId").ValueKind);
    }

    [Fact]
    public async Task Listar_Con_SucursalId_No_Filtra_Fuga_De_Datos_Entre_Sucursales()
    {
        // El filtro opcional ?sucursalId= (F1-ADM-01 Fase 2/4) sobre el
        // catálogo plano de puestos debe restringir estrictamente a los
        // puestos asignados a esa sucursal, no mezclar con los de otra
        // sucursal de la misma empresa.
        var client = await CreateSuperAdminClientAsync();
        var sucursalA = await CrearSucursalAsync(client, "PSTF-A");
        var sucursalB = await CrearSucursalAsync(client, "PSTF-B");
        var puestoA = await CrearPuestoAsync(client, "PSTF-PA");
        var puestoB = await CrearPuestoAsync(client, "PSTF-PB");
        await AsignarASucursalAsync(client, sucursalA, puestoA);
        await AsignarASucursalAsync(client, sucursalB, puestoB);

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

        Assert.Contains(puestoA, idsA);
        Assert.DoesNotContain(puestoB, idsA);
        Assert.Contains(puestoB, idsB);
        Assert.DoesNotContain(puestoA, idsB);
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

    [Fact]
    public async Task Crear_Puesto_Con_DepartamentoId_Valido_Retorna_201_Con_DepartamentoNombre()
    {
        var client = await CreateSuperAdminClientAsync();
        var deptoClave = RandomClave("DEP");
        var deptoResp = await client.PostAsJsonAsync("/api/v1/admin/departamentos", new
        {
            Id = Guid.Empty,
            Clave = deptoClave,
            Nombre = $"Depto {deptoClave}",
        });
        deptoResp.EnsureSuccessStatusCode();
        var deptoBody = await ReadJsonAsync(deptoResp);
        var deptoId = deptoBody.GetProperty("id").GetGuid();

        var puestoClave = RandomClave("PST");
        var response = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            Clave = puestoClave,
            Nombre = $"Puesto {puestoClave}",
            DepartamentoId = deptoId,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(deptoId, body.GetProperty("departamentoId").GetGuid());
        Assert.Equal($"Depto {deptoClave}", body.GetProperty("departamentoNombre").GetString());
    }

    [Fact]
    public async Task Crear_Puesto_Con_DepartamentoId_Inexistente_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            Clave = RandomClave("PST"),
            Nombre = "Puesto Invalido",
            DepartamentoId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("DEPARTAMENTO_INVALIDO", body.GetProperty("code").GetString());
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

    private static async Task<Guid> CrearPuestoAsync(HttpClient client, string prefix)
    {
        var clave = RandomClave(prefix);
        var response = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = $"Puesto {clave}",
        });
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CrearDepartamentoAsync(HttpClient client, string prefix = "DEP")
    {
        var clave = RandomClave(prefix);
        var response = await client.PostAsJsonAsync("/api/v1/admin/departamentos", new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = $"Departamento {clave}",
        });
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        return body.GetProperty("id").GetGuid();
    }

    private static async Task AsignarASucursalAsync(HttpClient client, Guid sucursalId, Guid puestoId, Guid? deptoId = null)
    {
        deptoId ??= await CrearDepartamentoAsync(client);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/empresas/sucursales/{sucursalId}/puestos/{puestoId}",
            new { DepartamentoId = deptoId.Value });
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
