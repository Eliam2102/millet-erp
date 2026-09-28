using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Escenarios de aceptación de F1-ADM-01.4 reabierta (pedido del owner
/// 2026-09-24): un mismo puesto genérico ("Gerente") se asigna a varios
/// departamentos de una sucursal, en vez de crear un puesto por
/// departamento. Cubre el plan E4:
/// <list type="bullet">
///   <item>Gerente asignado a Almacén y Contabilidad de la misma
///         sucursal (201/201); repetir Almacén → 409; desactivar sólo
///         Almacén deja Contabilidad activa.</item>
///   <item>Empleado con puesto Gerente y departamento explícito
///         Contabilidad → alta OK.</item>
///   <item>Empleado con puesto Gerente SIN departamento (el puesto tiene
///         2 asignaciones activas en la sucursal) → 422
///         <c>EMPLEADO_DEPARTAMENTO_REQUERIDO_PARA_PUESTO</c>.</item>
///   <item>Empleado con puesto Gerente y un departamento al que NO está
///         asignado → 422 (código existente,
///         <c>EMPLEADO_PUESTO_NO_ASIGNADO_A_SUCURSAL</c>).</item>
///   <item>Rol sugerido efectivo: asignación con rol propio vs heredado
///         del puesto.</item>
/// </list>
/// </summary>
public class SucursalPuestoVariosDepartamentosTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string EmpresasBase = "/api/v1/admin/empresas";
    private const string EmpleadosBase = "/api/v1/admin/empleados";
    private static readonly Guid EmpresaInicialId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    private readonly WebApplicationFactory<Program> _factory;

    public SucursalPuestoVariosDepartamentosTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Gerente_Se_Asigna_A_Almacen_Y_Contabilidad_De_La_Misma_Sucursal()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var gerenteId = await CrearPuestoAsync(client, "GER");
        var almacenId = await CrearYAsignarDepartamentoAsync(client, sucursalId, "ALM");
        var contabilidadId = await CrearYAsignarDepartamentoAsync(client, sucursalId, "CTB");

        var asignarAlmacen = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{gerenteId}",
            new { DepartamentoId = almacenId });
        var asignarContabilidad = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{gerenteId}",
            new { DepartamentoId = contabilidadId });

        Assert.Equal(HttpStatusCode.Created, asignarAlmacen.StatusCode);
        Assert.Equal(HttpStatusCode.Created, asignarContabilidad.StatusCode);

        // Repetir el mismo departamento → 409 SUCURSAL_PUESTO_DUPLICADA.
        var repetido = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{gerenteId}",
            new { DepartamentoId = almacenId });
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal(
            "SUCURSAL_PUESTO_DUPLICADA",
            (await ReadJsonAsync(repetido)).GetProperty("code").GetString());

        // Desactivar sólo Almacén deja Contabilidad activa.
        var desactivarAlmacen = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{gerenteId}/departamentos/{almacenId}/desactivar",
            content: null);
        Assert.Equal(HttpStatusCode.OK, desactivarAlmacen.StatusCode);

        var listado = await client.GetAsync($"{EmpresasBase}/sucursales/{sucursalId}/puestos");
        var items = (await ReadJsonAsync(listado)).GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("puestoId").GetGuid() == gerenteId)
            .ToDictionary(i => i.GetProperty("departamentoId").GetGuid(), i => i.GetProperty("estatus").GetInt32());
        Assert.Equal(1, items[almacenId]); // Inactivo
        Assert.Equal(0, items[contabilidadId]); // Activo
    }

    [Fact]
    public async Task Empleado_Gerente_Con_Departamento_Explicito_Se_Da_De_Alta_Correctamente()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var gerenteId = await CrearPuestoAsync(client, "GER");
        var almacenId = await CrearYAsignarDepartamentoAsync(client, sucursalId, "ALM");
        var contabilidadId = await CrearYAsignarDepartamentoAsync(client, sucursalId, "CTB");
        await AsignarPuestoADepartamentoAsync(client, sucursalId, gerenteId, almacenId);
        await AsignarPuestoADepartamentoAsync(client, sucursalId, gerenteId, contabilidadId);

        var alta = await client.PostAsJsonAsync(EmpleadosBase, new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaInicialId,
            Clave = $"GER-{Guid.NewGuid():N}"[..12],
            Nombre = "Gerente de Contabilidad",
            PuestoId = gerenteId,
            SucursalId = sucursalId,
            DepartamentoId = contabilidadId,
        });

        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var body = await ReadJsonAsync(alta);
        Assert.Equal(contabilidadId, body.GetProperty("departamentoId").GetGuid());
    }

    [Fact]
    public async Task Empleado_Gerente_Sin_Departamento_Con_Dos_Asignaciones_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var gerenteId = await CrearPuestoAsync(client, "GER");
        var almacenId = await CrearYAsignarDepartamentoAsync(client, sucursalId, "ALM");
        var contabilidadId = await CrearYAsignarDepartamentoAsync(client, sucursalId, "CTB");
        await AsignarPuestoADepartamentoAsync(client, sucursalId, gerenteId, almacenId);
        await AsignarPuestoADepartamentoAsync(client, sucursalId, gerenteId, contabilidadId);

        var alta = await client.PostAsJsonAsync(EmpleadosBase, new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaInicialId,
            Clave = $"GER-{Guid.NewGuid():N}"[..12],
            Nombre = "Gerente sin departamento",
            PuestoId = gerenteId,
            SucursalId = sucursalId,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, alta.StatusCode);
        Assert.Equal(
            "EMPLEADO_DEPARTAMENTO_REQUERIDO_PARA_PUESTO",
            (await ReadJsonAsync(alta)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Empleado_Gerente_Sin_Departamento_Con_Una_Sola_Asignacion_Se_Da_De_Alta_Correctamente()
    {
        // Caso implícito: si el puesto sólo tiene una asignación activa en
        // la sucursal, no hace falta especificar el departamento.
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var gerenteId = await CrearPuestoAsync(client, "GER");
        var almacenId = await CrearYAsignarDepartamentoAsync(client, sucursalId, "ALM");
        await AsignarPuestoADepartamentoAsync(client, sucursalId, gerenteId, almacenId);

        var alta = await client.PostAsJsonAsync(EmpleadosBase, new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaInicialId,
            Clave = $"GER-{Guid.NewGuid():N}"[..12],
            Nombre = "Gerente único departamento",
            PuestoId = gerenteId,
            SucursalId = sucursalId,
        });

        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
    }

    [Fact]
    public async Task Empleado_Gerente_Con_Departamento_No_Asignado_Retorna_422_Existente()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var gerenteId = await CrearPuestoAsync(client, "GER");
        var almacenId = await CrearYAsignarDepartamentoAsync(client, sucursalId, "ALM");
        var ventasId = await CrearYAsignarDepartamentoAsync(client, sucursalId, "VTA");
        // El puesto sólo se asigna a Almacén; Ventas queda sin asignación de Gerente.
        await AsignarPuestoADepartamentoAsync(client, sucursalId, gerenteId, almacenId);

        var alta = await client.PostAsJsonAsync(EmpleadosBase, new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaInicialId,
            Clave = $"GER-{Guid.NewGuid():N}"[..12],
            Nombre = "Gerente en departamento no asignado",
            PuestoId = gerenteId,
            SucursalId = sucursalId,
            DepartamentoId = ventasId,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, alta.StatusCode);
        Assert.Equal(
            "EMPLEADO_PUESTO_NO_ASIGNADO_A_SUCURSAL",
            (await ReadJsonAsync(alta)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task RolSugeridoEfectivo_Usa_El_Propio_De_La_Asignacion_Cuando_Existe()
    {
        var client = await CreateSuperAdminClientAsync();
        var roles = await ObtenerDosRolesDistintosAsync(client);
        var rolDelPuesto = roles.Item1;
        var rolDeLaAsignacion = roles.Item2;

        var sucursalId = await CrearSucursalAsync(client);
        var puestoId = await CrearPuestoConRolSugeridoAsync(client, rolDelPuesto, "GER-R");
        var deptoId = await CrearYAsignarDepartamentoAsync(client, sucursalId, "OPR");

        var respuesta = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            new { DepartamentoId = deptoId, RolSugeridoId = rolDeLaAsignacion });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var body = await ReadJsonAsync(respuesta);
        Assert.Equal(rolDeLaAsignacion, body.GetProperty("rolSugeridoId").GetGuid());
        Assert.Equal(rolDeLaAsignacion, body.GetProperty("rolSugeridoEfectivoId").GetGuid());
    }

    [Fact]
    public async Task RolSugeridoEfectivo_Hereda_Del_Puesto_Cuando_La_Asignacion_No_Tiene_Excepcion()
    {
        var client = await CreateSuperAdminClientAsync();
        var rolDelPuesto = await ObtenerPrimerRolIdAsync(client);

        var sucursalId = await CrearSucursalAsync(client);
        var puestoId = await CrearPuestoConRolSugeridoAsync(client, rolDelPuesto, "GER-H");
        var deptoId = await CrearYAsignarDepartamentoAsync(client, sucursalId, "OPR");

        var respuesta = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            new { DepartamentoId = deptoId });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var body = await ReadJsonAsync(respuesta);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("rolSugeridoId").ValueKind);
        Assert.Equal(rolDelPuesto, body.GetProperty("rolSugeridoEfectivoId").GetGuid());
    }

    // --- Helpers ---

    private static string RandomClave(string prefix)
    {
        var hex = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        return $"{prefix}-{hex}";
    }

    private static async Task<Guid> CrearSucursalAsync(HttpClient client, string prefix = "SVD")
    {
        var clave = RandomClave(prefix);
        var response = await client.PostAsJsonAsync($"{EmpresasBase}/sucursales", new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = $"Sucursal {clave}",
        });
        response.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CrearDepartamentoAsync(HttpClient client, string prefix)
    {
        var clave = RandomClave(prefix);
        var response = await client.PostAsJsonAsync("/api/v1/admin/departamentos", new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = $"Departamento {clave}",
        });
        response.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CrearPuestoAsync(HttpClient client, string prefix)
    {
        var clave = RandomClave(prefix);
        var response = await client.PostAsJsonAsync("/api/v1/admin/puestos", new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = $"Puesto {clave}",
        });
        response.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CrearPuestoConRolSugeridoAsync(HttpClient client, Guid rolSugeridoId, string prefix)
    {
        var clave = RandomClave(prefix);
        var response = await client.PostAsJsonAsync("/api/v1/admin/puestos", new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = $"Puesto {clave}",
            RolSugeridoId = rolSugeridoId,
        });
        response.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CrearYAsignarDepartamentoAsync(HttpClient client, Guid sucursalId, string prefix)
    {
        var deptoId = await CrearDepartamentoAsync(client, prefix);
        var res = await client.PostAsync($"{EmpresasBase}/sucursales/{sucursalId}/departamentos/{deptoId}", null);
        res.EnsureSuccessStatusCode();
        return deptoId;
    }

    private static async Task AsignarPuestoADepartamentoAsync(
        HttpClient client, Guid sucursalId, Guid puestoId, Guid departamentoId)
    {
        var response = await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            new { DepartamentoId = departamentoId });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> ObtenerPrimerRolIdAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/identidad/roles");
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        return body.GetProperty("items").EnumerateArray().First().GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Devuelve dos roles distintos: si el catálogo sólo tiene uno
    /// sembrado (típico de una BD de pruebas nueva), crea uno adicional
    /// vía <c>POST /api/v1/identidad/roles</c> (SuperAdmin tiene el
    /// permiso de sobra).
    /// </summary>
    private static async Task<(Guid, Guid)> ObtenerDosRolesDistintosAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/identidad/roles?limit=200");
        response.EnsureSuccessStatusCode();
        var items = (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray().ToList();
        var primero = items[0].GetProperty("id").GetGuid();

        if (items.Count > 1)
        {
            return (primero, items[1].GetProperty("id").GetGuid());
        }

        var codigo = $"test-rol-vd-{Guid.NewGuid():N}"[..24];
        var crear = await client.PostAsJsonAsync("/api/v1/identidad/roles", new
        {
            Id = Guid.Empty,
            Codigo = codigo,
            Nombre = "Rol de prueba puesto varios departamentos",
            Descripcion = (string?)null,
        });
        crear.EnsureSuccessStatusCode();
        var segundo = (await ReadJsonAsync(crear)).GetProperty("id").GetGuid();
        return (primero, segundo);
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
