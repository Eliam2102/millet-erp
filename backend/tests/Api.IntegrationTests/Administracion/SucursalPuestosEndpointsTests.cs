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
        var deptoId = await CrearYAsignarDepartamentoAsync(client, sucursalId);

        var response = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            new { DepartamentoId = deptoId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(sucursalId, body.GetProperty("sucursalId").GetGuid());
        Assert.Equal(puestoId, body.GetProperty("puestoId").GetGuid());
        Assert.Equal(deptoId, body.GetProperty("departamentoId").GetGuid());
        Assert.Equal(0, body.GetProperty("estatus").GetInt32()); // EstatusCatalogo.Activo
    }

    [Fact]
    public async Task Asignar_Duplicado_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var puestoId = await CrearPuestoAsync(client);
        var deptoId = await CrearYAsignarDepartamentoAsync(client, sucursalId);

        var primero = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            new { DepartamentoId = deptoId });
        primero.EnsureSuccessStatusCode();

        var duplicado = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            new { DepartamentoId = deptoId });

        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);
    }

    [Fact]
    public async Task Asignar_Puesto_De_OtraEmpresa_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var deptoId = await CrearYAsignarDepartamentoAsync(client, sucursalId);
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

        var response = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            new { DepartamentoId = deptoId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("RELACION_INVALIDA", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Asignar_Con_Departamento_De_OtraEmpresa_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var puestoId = await CrearPuestoAsync(client);

        var otraEmpresa = await client.PostAsJsonAsync(EmpresasBase, new
        {
            Id = Guid.Empty,
            Rfc = ("TST" + Guid.NewGuid().ToString("N"))[..13].ToUpperInvariant(),
            RazonSocial = "Empresa ficticia para depto ajeno",
            RegimenFiscal = "601",
        });
        otraEmpresa.EnsureSuccessStatusCode();
        var otraEmpresaId = (await ReadJsonAsync(otraEmpresa)).GetProperty("id").GetGuid();
        Guid deptoAjenoId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
            using var bypass = empresaContext.Bypass();
            deptoAjenoId = Guid.CreateVersion7();
            db.Departamentos.Add(new Departamento(
                deptoAjenoId,
                otraEmpresaId,
                RandomClave("OTRAD"),
                "Depto de otra empresa"));
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            new { DepartamentoId = deptoAjenoId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("DEPARTAMENTO_OTRA_EMPRESA", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Asignar_Con_Departamento_Inactivo_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var puestoId = await CrearPuestoAsync(client);
        var deptoId = await CrearYAsignarDepartamentoAsync(client, sucursalId);

        var desactResp = await client.PostAsync(
            $"/api/v1/admin/departamentos/{deptoId}/desactivar",
            content: null);
        desactResp.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            new { DepartamentoId = deptoId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("DEPARTAMENTO_INACTIVO", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Asignar_Con_Departamento_No_Asignado_A_Sucursal_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var puestoId = await CrearPuestoAsync(client);
        var deptoId = await CrearDepartamentoAsync(client);

        var response = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            new { DepartamentoId = deptoId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("DEPARTAMENTO_NO_ASIGNADO_A_SUCURSAL", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Asignar_Con_Departamento_Inactivo_En_Sucursal_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var puestoId = await CrearPuestoAsync(client);
        var deptoId = await CrearYAsignarDepartamentoAsync(client, sucursalId);

        var desactResp = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/departamentos/{deptoId}/desactivar",
            content: null);
        desactResp.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            new { DepartamentoId = deptoId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("DEPARTAMENTO_SUCURSAL_INACTIVO", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Asignar_A_Sucursal_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var puestoId = await CrearPuestoAsync(client);
        var deptoId = await CrearDepartamentoAsync(client);

        var response = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/puestos/{puestoId}",
            new { DepartamentoId = deptoId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Asignar_Puesto_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var deptoId = await CrearYAsignarDepartamentoAsync(client, sucursalId);

        var response = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{Guid.NewGuid()}",
            new { DepartamentoId = deptoId });

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
        var deptoId = await AsignarAsync(client, sucursalId, puestoId);

        var response = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}/departamentos/{deptoId}/desactivar",
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
        var deptoId = await AsignarAsync(client, sucursalId, puestoId);

        var desactivada = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}/departamentos/{deptoId}/desactivar",
            content: null);
        desactivada.EnsureSuccessStatusCode();

        var reactivada = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}/departamentos/{deptoId}/reactivar",
            content: null);

        Assert.Equal(HttpStatusCode.OK, reactivada.StatusCode);
        var body = await ReadJsonAsync(reactivada);
        Assert.Equal(0, body.GetProperty("estatus").GetInt32()); // EstatusCatalogo.Activo
    }

    [Fact]
    public async Task Asignar_Mismo_Puesto_A_Otro_Departamento_De_La_Sucursal_Retorna_201()
    {
        // F1-ADM-01.4 reabierta: un puesto genérico ("Gerente") puede
        // asignarse a varios departamentos activos de la misma sucursal.
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var puestoId = await CrearPuestoAsync(client, "GER");
        var deptoAlmacen = await CrearYAsignarDepartamentoAsync(client, sucursalId, "ALM");
        var deptoContabilidad = await CrearYAsignarDepartamentoAsync(client, sucursalId, "CTB");

        var primero = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            new { DepartamentoId = deptoAlmacen });
        var segundo = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            new { DepartamentoId = deptoContabilidad });

        Assert.Equal(HttpStatusCode.Created, primero.StatusCode);
        Assert.Equal(HttpStatusCode.Created, segundo.StatusCode);

        var listado = await client.GetAsync($"{EmpresasBase}/sucursales/{sucursalId}/puestos");
        var body = await ReadJsonAsync(listado);
        var deptos = body.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("puestoId").GetGuid() == puestoId)
            .Select(i => i.GetProperty("departamentoId").GetGuid())
            .ToList();
        Assert.Contains(deptoAlmacen, deptos);
        Assert.Contains(deptoContabilidad, deptos);
    }

    [Fact]
    public async Task Desactivar_Un_Departamento_No_Afecta_Otras_Asignaciones_Activas_Del_Puesto()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var puestoId = await CrearPuestoAsync(client, "GER");
        var deptoAlmacen = await CrearYAsignarDepartamentoAsync(client, sucursalId, "ALM");
        var deptoContabilidad = await CrearYAsignarDepartamentoAsync(client, sucursalId, "CTB");
        (await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            new { DepartamentoId = deptoAlmacen })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            new { DepartamentoId = deptoContabilidad })).EnsureSuccessStatusCode();

        var desactivar = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}/departamentos/{deptoAlmacen}/desactivar",
            content: null);
        Assert.Equal(HttpStatusCode.OK, desactivar.StatusCode);

        var listado = await client.GetAsync($"{EmpresasBase}/sucursales/{sucursalId}/puestos");
        var body = await ReadJsonAsync(listado);
        var items = body.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("puestoId").GetGuid() == puestoId)
            .ToDictionary(i => i.GetProperty("departamentoId").GetGuid(), i => i.GetProperty("estatus").GetInt32());
        Assert.Equal(1, items[deptoAlmacen]); // Inactivo
        Assert.Equal(0, items[deptoContabilidad]); // sigue Activo
    }

    [Fact]
    public async Task Asignar_Con_RolSugerido_Valido_Retorna_201_Con_RolEfectivo()
    {
        var client = await CreateSuperAdminClientAsync();
        var rolId = await ObtenerPrimerRolIdAsync(client);
        var sucursalId = await CrearSucursalAsync(client);
        var puestoId = await CrearPuestoAsync(client);
        var deptoId = await CrearYAsignarDepartamentoAsync(client, sucursalId);

        var response = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            new { DepartamentoId = deptoId, RolSugeridoId = rolId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(rolId, body.GetProperty("rolSugeridoId").GetGuid());
        Assert.Equal(rolId, body.GetProperty("rolSugeridoEfectivoId").GetGuid());
    }

    [Fact]
    public async Task Asignar_Con_RolSugerido_Inexistente_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var puestoId = await CrearPuestoAsync(client);
        var deptoId = await CrearYAsignarDepartamentoAsync(client, sucursalId);

        var response = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            new { DepartamentoId = deptoId, RolSugeridoId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("ROL_SUGERIDO_INVALIDO", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Patch_RolSugerido_Fija_Y_Limpia_La_Excepcion_De_La_Asignacion()
    {
        var client = await CreateSuperAdminClientAsync();
        var rolId = await ObtenerPrimerRolIdAsync(client);
        var sucursalId = await CrearSucursalAsync(client);
        var puestoId = await CrearPuestoAsync(client);
        var deptoId = await AsignarAsync(client, sucursalId, puestoId);

        var fijar = await client.PatchAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}/departamentos/{deptoId}",
            new { RolSugeridoId = rolId });
        Assert.Equal(HttpStatusCode.OK, fijar.StatusCode);
        var fijarBody = await ReadJsonAsync(fijar);
        Assert.Equal(rolId, fijarBody.GetProperty("rolSugeridoId").GetGuid());
        Assert.Equal(rolId, fijarBody.GetProperty("rolSugeridoEfectivoId").GetGuid());

        var limpiar = await client.PatchAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}/departamentos/{deptoId}",
            new { RolSugeridoId = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, limpiar.StatusCode);
        var limpiarBody = await ReadJsonAsync(limpiar);
        Assert.Equal(JsonValueKind.Null, limpiarBody.GetProperty("rolSugeridoId").ValueKind);
    }

    [Fact]
    public async Task Patch_RolSugerido_Con_Rol_Inexistente_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var puestoId = await CrearPuestoAsync(client);
        var deptoId = await AsignarAsync(client, sucursalId, puestoId);

        var response = await client.PatchAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}/departamentos/{deptoId}",
            new { RolSugeridoId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("ROL_SUGERIDO_INVALIDO", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Patch_RolSugerido_De_Asignacion_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.PatchAsJsonAsync(
            $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/puestos/{Guid.NewGuid()}/departamentos/{Guid.NewGuid()}",
            new { RolSugeridoId = (Guid?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
    [InlineData("")]                              // asignar
    [InlineData("/departamentos/{0}/desactivar")]
    [InlineData("/departamentos/{0}/reactivar")]
    public async Task Mutaciones_Sin_Permiso_Retornan_403(string plantillaSufijo)
    {
        var client = await CreateNoPermsClientAsync();
        var sufijo = string.Format(plantillaSufijo, Guid.NewGuid());
        var url = $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/puestos/{Guid.NewGuid()}{sufijo}";
        HttpContent? content = sufijo == "" ? JsonContent.Create(new { DepartamentoId = Guid.NewGuid() }) : null;
        var response = await client.PostAsync(url, content);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("")]                              // asignar
    [InlineData("/departamentos/{0}/desactivar")]
    [InlineData("/departamentos/{0}/reactivar")]
    public async Task Mutaciones_Sin_Token_Retornan_401(string plantillaSufijo)
    {
        var client = _factory.CreateClientWithIdempotency();
        var sufijo = string.Format(plantillaSufijo, Guid.NewGuid());
        var url = $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/puestos/{Guid.NewGuid()}{sufijo}";
        HttpContent? content = sufijo == "" ? JsonContent.Create(new { DepartamentoId = Guid.NewGuid() }) : null;
        var response = await client.PostAsync(url, content);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Patch_RolSugerido_Sin_Permiso_Retorna_403()
    {
        var client = await CreateNoPermsClientAsync();
        var url = $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/puestos/{Guid.NewGuid()}" +
                  $"/departamentos/{Guid.NewGuid()}";
        var response = await client.PatchAsJsonAsync(url, new { RolSugeridoId = (Guid?)null });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Patch_RolSugerido_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClientWithIdempotency();
        var url = $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/puestos/{Guid.NewGuid()}" +
                  $"/departamentos/{Guid.NewGuid()}";
        var response = await client.PatchAsJsonAsync(url, new { RolSugeridoId = (Guid?)null });
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

    private static async Task<Guid> CrearDepartamentoAsync(HttpClient client, string prefix = "DXP")
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

    private static async Task<Guid> CrearYAsignarDepartamentoAsync(HttpClient client, Guid sucursalId, string prefix = "DXP")
    {
        var deptoId = await CrearDepartamentoAsync(client, prefix);
        var res = await client.PostAsync($"{EmpresasBase}/sucursales/{sucursalId}/departamentos/{deptoId}", null);
        res.EnsureSuccessStatusCode();
        return deptoId;
    }

    private static async Task<Guid> AsignarAsync(HttpClient client, Guid sucursalId, Guid puestoId, Guid? departamentoId = null)
    {
        departamentoId ??= await CrearYAsignarDepartamentoAsync(client, sucursalId);
        var response = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            new { DepartamentoId = departamentoId.Value });
        response.EnsureSuccessStatusCode();
        return departamentoId.Value;
    }

    private static async Task<Guid> ObtenerPrimerRolIdAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/identidad/roles");
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        return body.GetProperty("items").EnumerateArray().First().GetProperty("id").GetGuid();
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
