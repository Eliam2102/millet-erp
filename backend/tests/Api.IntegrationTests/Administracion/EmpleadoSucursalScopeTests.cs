using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Alcance por sucursal de las escrituras sobre empleados (ADR-0051;
/// criterios 01-13 y recorrido R4 del documento de aceptación ADM-01):
/// una persona operativa de S1 no lee ni modifica empleados de S2 y recibe
/// 403 sin cambios en BD; la corporativa con
/// <c>admin.empleados.gestionar-todas-sucursales</c> sí puede.
/// </summary>
public class EmpleadoSucursalScopeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string EmpleadosBase = "/api/v1/admin/empleados";
    private const string ColaboradoresBase = "/api/v1/admin/colaboradores";
    private static readonly Guid EmpresaInicialId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    private readonly WebApplicationFactory<Program> _factory;

    public EmpleadoSucursalScopeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Operativo_No_Edita_Empleado_De_Sucursal_Ajena()
    {
        var (propia, ajena) = await CrearSucursalesAsync();
        var empleadoAjeno = await SeedEmpleadoAsync(ajena, "Empleado ajeno");
        var operativo = await CreateOperativoAsync(propia);

        var response = await operativo.PatchAsJsonAsync(
            $"{EmpleadosBase}/{empleadoAjeno}", new { Nombre = "Nombre cambiado" });

        await AssertForbiddenAsync(response);
        Assert.Equal("Empleado ajeno", (await LeerEmpleadoAsync(empleadoAjeno)).Nombre);
    }

    [Fact]
    public async Task Operativo_No_Da_De_Baja_Empleado_De_Sucursal_Ajena()
    {
        var (propia, ajena) = await CrearSucursalesAsync();
        var empleadoAjeno = await SeedEmpleadoAsync(ajena, "Empleado ajeno baja");
        var operativo = await CreateOperativoAsync(propia);

        var response = await operativo.PostAsync($"{EmpleadosBase}/{empleadoAjeno}/desactivar", content: null);

        await AssertForbiddenAsync(response);
        Assert.Equal(EstatusCatalogo.Activo, (await LeerEmpleadoAsync(empleadoAjeno)).Estatus);
    }

    [Fact]
    public async Task Operativo_No_Reactiva_Empleado_De_Sucursal_Ajena()
    {
        var (propia, ajena) = await CrearSucursalesAsync();
        var empleadoAjeno = await SeedEmpleadoAsync(ajena, "Empleado ajeno reactivar", activo: false);
        var operativo = await CreateOperativoAsync(propia);

        var response = await operativo.PostAsync($"{EmpleadosBase}/{empleadoAjeno}/reactivar", content: null);

        await AssertForbiddenAsync(response);
        Assert.Equal(EstatusCatalogo.Inactivo, (await LeerEmpleadoAsync(empleadoAjeno)).Estatus);
    }

    [Fact]
    public async Task Operativo_No_Consulta_Ni_Da_Acceso_A_Empleado_De_Sucursal_Ajena()
    {
        var (propia, ajena) = await CrearSucursalesAsync();
        var empleadoAjeno = await SeedEmpleadoAsync(ajena, "Empleado ajeno acceso");
        var operativo = await CreateOperativoAsync(propia);

        await AssertForbiddenAsync(await operativo.GetAsync($"{ColaboradoresBase}/{empleadoAjeno}/acceso"));
        await AssertForbiddenAsync(await operativo.PostAsJsonAsync(
            $"{ColaboradoresBase}/{empleadoAjeno}/acceso",
            new { Acceso = 1, CorreoCorporativo = "ajeno@millet.test" }));
        await AssertForbiddenAsync(await operativo.PostAsync(
            $"{ColaboradoresBase}/{empleadoAjeno}/acceso/reintentar", content: null));
        await AssertForbiddenAsync(await operativo.PostAsync(
            $"{ColaboradoresBase}/{empleadoAjeno}/acceso/reenviar", content: null));
        Assert.Null((await LeerEmpleadoAsync(empleadoAjeno)).UsuarioId);
    }

    [Fact]
    public async Task Operativo_No_Transfiere_Empleado_A_Sucursal_No_Asociada()
    {
        var (propia, ajena) = await CrearSucursalesAsync();
        var empleadoPropio = await SeedEmpleadoAsync(propia, "Empleado propio");
        var operativo = await CreateOperativoAsync(propia);

        var response = await operativo.PatchAsJsonAsync(
            $"{EmpleadosBase}/{empleadoPropio}", new { SucursalId = ajena });

        await AssertForbiddenAsync(response);
        Assert.Equal(propia, (await LeerEmpleadoAsync(empleadoPropio)).SucursalId);
    }

    [Fact]
    public async Task Operativo_No_Da_De_Alta_Colaborador_En_Sucursal_Ajena()
    {
        var (propia, ajena) = await CrearSucursalesAsync();
        var operativo = await CreateOperativoAsync(propia);
        var clave = $"SCA-{Guid.NewGuid():N}"[..12];

        // El guard corre antes de las validaciones de negocio: aun con
        // departamento/puesto inexistentes, la respuesta es 403 (no 404/422).
        var response = await operativo.PostAsJsonAsync(ColaboradoresBase, new
        {
            Id = Guid.CreateVersion7(),
            Clave = clave,
            Nombre = "Alta en sucursal ajena",
            SucursalId = ajena,
            DepartamentoId = Guid.NewGuid(),
            PuestoId = Guid.NewGuid(),
            Acceso = 0,
        });

        await AssertForbiddenAsync(response);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        Assert.False(await db.Empleados.AnyAsync(e => e.Clave == clave));
    }

    [Fact]
    public async Task Operativo_Edita_Empleado_De_Su_Sucursal()
    {
        var (propia, _) = await CrearSucursalesAsync();
        var empleadoPropio = await SeedEmpleadoAsync(propia, "Empleado propio edita");
        var operativo = await CreateOperativoAsync(propia);

        var response = await operativo.PatchAsJsonAsync(
            $"{EmpleadosBase}/{empleadoPropio}", new { Nombre = "Empleado propio editado" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Empleado propio editado", (await LeerEmpleadoAsync(empleadoPropio)).Nombre);
    }

    [Fact]
    public async Task Corporativo_Con_Bypass_Edita_Empleado_De_Cualquier_Sucursal()
    {
        var (_, ajena) = await CrearSucursalesAsync();
        var empleadoAjeno = await SeedEmpleadoAsync(ajena, "Empleado para corporativo");
        var (corporativo, _) = await CreateUsuarioConPermisosAsync(
            PermisosCanonicos.AdminEmpleadosGestionar,
            PermisosCanonicos.AdminEmpleadosGestionarTodasSucursales);

        var response = await corporativo.PatchAsJsonAsync(
            $"{EmpleadosBase}/{empleadoAjeno}", new { Nombre = "Editado por corporativo" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Editado por corporativo", (await LeerEmpleadoAsync(empleadoAjeno)).Nombre);
    }

    // Auditoría (criterio 01-14): alta, cambio y baja quedan en la bitácora
    // con actor, entidad y sucursal, y se encuentran filtrando por sucursal.
    [Fact]
    public async Task Alta_Cambio_Y_Baja_Quedan_En_Bitacora_De_La_Sucursal()
    {
        var (propia, _) = await CrearSucursalesAsync();
        var (operativo, operativoId) = await CreateUsuarioConPermisosAsync(PermisosCanonicos.AdminEmpleadosGestionar);
        using (var scope = _factory.Services.CreateScope())
        {
            var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            identidad.UsuarioSucursales.Add(new UsuarioSucursal(
                Guid.CreateVersion7(), operativoId, propia, EmpresaInicialId));
            await identidad.SaveChangesAsync();
        }

        var alta = await operativo.PostAsJsonAsync(EmpleadosBase, new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaInicialId,
            Clave = $"EB-{Guid.NewGuid():N}"[..12],
            Nombre = "Empleado bitácora",
            SucursalId = propia,
        });
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var empleadoId = (await ReadJsonAsync(alta)).GetProperty("id").GetGuid();
        (await operativo.PatchAsJsonAsync($"{EmpleadosBase}/{empleadoId}", new { Nombre = "Empleado bitácora 2" }))
            .EnsureSuccessStatusCode();
        (await operativo.PostAsync($"{EmpleadosBase}/{empleadoId}/desactivar", content: null))
            .EnsureSuccessStatusCode();

        var admin = _factory.CreateClientWithIdempotency();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await FakeLoginAsync(admin, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev"));
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var response = await admin.GetAsync(
            $"/api/v1/admin/auditoria?desde={hoy.AddDays(-1):yyyy-MM-dd}&hasta={hoy.AddDays(1):yyyy-MM-dd}" +
            $"&sucursalId={propia}&usuarioId={operativoId}&limit=200");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var entradas = (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray()
            .Where(e => e.GetProperty("entidadId").ValueKind != JsonValueKind.Null
                && e.GetProperty("entidadId").GetGuid() == empleadoId)
            .ToList();
        Assert.True(entradas.Count >= 3, $"Se esperaban alta, cambio y baja; hubo {entradas.Count}.");
        Assert.All(entradas, e =>
        {
            Assert.Equal(operativoId, e.GetProperty("usuarioId").GetGuid());
            Assert.Equal(propia, e.GetProperty("sucursalId").GetGuid());
            Assert.False(string.IsNullOrEmpty(e.GetProperty("sucursalClave").GetString()));
        });
    }

    // --- Helpers ---

    private async Task<HttpClient> CreateOperativoAsync(Guid sucursalId)
    {
        var (client, usuarioId) = await CreateUsuarioConPermisosAsync(
            PermisosCanonicos.AdminEmpleadosGestionar,
            PermisosCanonicos.IdentidadUsuariosCrear,
            PermisosCanonicos.IdentidadAsignacionesAdministrar);
        using var scope = _factory.Services.CreateScope();
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        identidad.UsuarioSucursales.Add(new UsuarioSucursal(
            Guid.CreateVersion7(), usuarioId, sucursalId, EmpresaInicialId));
        await identidad.SaveChangesAsync();
        return client;
    }

    private async Task<(HttpClient Client, Guid UsuarioId)> CreateUsuarioConPermisosAsync(
        params string[] permisoCodigos)
    {
        var random = Guid.NewGuid().ToString("N")[..10];
        var oid = $"test-emp-scope-{random}";
        var usuarioId = Guid.CreateVersion7();

        using (var scope = _factory.Services.CreateScope())
        {
            var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();

            var rolId = Guid.CreateVersion7();
            identidad.Roles.Add(new Rol(rolId, $"test-emp-scope-{random}", "Rol de prueba de alcance de empleados"));
            foreach (var codigo in permisoCodigos)
            {
                var permisoId = await identidad.Permisos.AsNoTracking()
                    .Where(p => p.Codigo == codigo).Select(p => p.Id).SingleAsync();
                identidad.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rolId, permisoId));
            }

            identidad.Usuarios.Add(new Usuario(usuarioId, oid, $"{oid}@test.local", "Usuario Alcance Empleados"));
            identidad.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId));
            identidad.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(
                Guid.CreateVersion7(), usuarioId, EmpresaInicialId, rolId, asignadoPorUsuarioId: null));
            await identidad.SaveChangesAsync();
        }

        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, oid, $"{oid}@test.local", "Usuario Alcance Empleados");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, usuarioId);
    }

    private async Task<(Guid Propia, Guid Ajena)> CrearSucursalesAsync()
    {
        var admin = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(admin, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await CrearSucursalAsync(admin, "ESP"), await CrearSucursalAsync(admin, "ESA"));
    }

    private static async Task<Guid> CrearSucursalAsync(HttpClient client, string prefix)
    {
        var clave = $"{prefix}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        var response = await client.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = $"Sucursal {clave}",
        });
        response.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> SeedEmpleadoAsync(Guid sucursalId, string nombre, bool activo = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var empleado = new Empleado(
            Guid.CreateVersion7(), EmpresaInicialId, $"ES-{Guid.NewGuid():N}"[..12], nombre, sucursalId: sucursalId);
        if (!activo) empleado.Desactivar();
        db.Empleados.Add(empleado);
        await db.SaveChangesAsync();
        return empleado.Id;
    }

    private async Task<Empleado> LeerEmpleadoAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await db.Empleados.AsNoTracking().SingleAsync(e => e.Id == id);
    }

    private static async Task AssertForbiddenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("SUCURSAL_NO_ASOCIADA", (await ReadJsonAsync(response)).GetProperty("code").GetString());
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
        return (await ReadJsonAsync(response)).GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("fake-login no devolvió accessToken");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
