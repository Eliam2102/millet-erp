using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Administracion.Domain;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Tests integration de asignaciones Usuario ↔ Sucursal (F1-ADM-01
/// Fase 2). Endpoint:
/// <c>/api/v1/admin/empresas/sucursales/{sucursalId}/usuarios</c>.
/// Sigue el patrón de <see cref="SucursalDepartamentosEndpointsTests"/>.
/// </summary>
public class SucursalUsuariosEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms-sxu";
    private const string EmpresasBase = "/api/v1/admin/empresas";
    private static readonly Guid EmpresaInicialId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    private readonly WebApplicationFactory<Program> _factory;

    public SucursalUsuariosEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Asignar_Con_Datos_Validos_Retorna_201()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var usuarioId = await SeedUsuarioConRolEnEmpresaAsync(EmpresaInicialId);

        var response = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/usuarios/{usuarioId}",
            content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(sucursalId, body.GetProperty("sucursalId").GetGuid());
        Assert.Equal(usuarioId, body.GetProperty("usuarioId").GetGuid());
        Assert.Equal(0, body.GetProperty("estatus").GetInt32()); // EstatusCatalogo.Activo
    }

    [Fact]
    public async Task Asignar_Usuario_Sin_Rol_En_Empresa_De_La_Sucursal_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client); // empresa inicial
        // No depender de que otra prueba haya creado una segunda empresa.
        // Es un tenant ficticio aislado, sólo para probar el guard técnico.
        var otraEmpresa = await client.PostAsJsonAsync(EmpresasBase, new
        {
            Id = Guid.Empty,
            Rfc = ("TST" + Guid.NewGuid().ToString("N"))[..13].ToUpperInvariant(),
            RazonSocial = "Otra empresa de prueba",
            RegimenFiscal = "601",
        });
        otraEmpresa.EnsureSuccessStatusCode();
        var otraEmpresaId = (await ReadJsonAsync(otraEmpresa)).GetProperty("id").GetGuid();

        // Usuario con rol SOLO en una empresa distinta a la dueña de la sucursal.
        var usuarioId = await SeedUsuarioConRolEnEmpresaAsync(otraEmpresaId);

        var response = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/usuarios/{usuarioId}",
            content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("RELACION_INVALIDA", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Asignar_Duplicado_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var usuarioId = await SeedUsuarioConRolEnEmpresaAsync(EmpresaInicialId);

        var primero = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/usuarios/{usuarioId}",
            content: null);
        primero.EnsureSuccessStatusCode();

        var duplicado = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/usuarios/{usuarioId}",
            content: null);

        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);
    }

    [Fact]
    public async Task Asignar_A_Sucursal_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var usuarioId = await SeedUsuarioConRolEnEmpresaAsync(EmpresaInicialId);

        var response = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/usuarios/{usuarioId}",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Asignar_Usuario_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);

        var response = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/usuarios/{Guid.NewGuid()}",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Listar_Retorna_200_Con_Asignaciones()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var usuarioId = await SeedUsuarioConRolEnEmpresaAsync(EmpresaInicialId);
        await AsignarAsync(client, sucursalId, usuarioId);

        var response = await client.GetAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/usuarios");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.GetProperty("total").GetInt32() >= 1);
    }

    [Fact]
    public async Task Listar_De_Sucursal_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync(
            $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/usuarios");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Listar_No_Filtra_Fuga_De_Datos_Entre_Sucursales()
    {
        // Aislamiento cross-sucursal: el listado de la sucursal A no debe
        // incluir usuarios asignados solo a la sucursal B, aunque ambas
        // pertenezcan a la misma empresa (F1-ADM-01 Fase 4).
        var client = await CreateSuperAdminClientAsync();
        var sucursalA = await CrearSucursalAsync(client, "SXU-A");
        var sucursalB = await CrearSucursalAsync(client, "SXU-B");
        var usuarioA = await SeedUsuarioConRolEnEmpresaAsync(EmpresaInicialId);
        var usuarioB = await SeedUsuarioConRolEnEmpresaAsync(EmpresaInicialId);
        await AsignarAsync(client, sucursalA, usuarioA);
        await AsignarAsync(client, sucursalB, usuarioB);

        var respuestaA = await client.GetAsync($"{EmpresasBase}/sucursales/{sucursalA}/usuarios");
        var respuestaB = await client.GetAsync($"{EmpresasBase}/sucursales/{sucursalB}/usuarios");

        Assert.Equal(HttpStatusCode.OK, respuestaA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, respuestaB.StatusCode);
        var bodyA = await ReadJsonAsync(respuestaA);
        var bodyB = await ReadJsonAsync(respuestaB);

        var idsA = bodyA.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("usuarioId").GetGuid())
            .ToList();
        var idsB = bodyB.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("usuarioId").GetGuid())
            .ToList();

        Assert.Contains(usuarioA, idsA);
        Assert.DoesNotContain(usuarioB, idsA);
        Assert.Contains(usuarioB, idsB);
        Assert.DoesNotContain(usuarioA, idsB);
    }

    [Fact]
    public async Task Desactivar_Retorna_200_Con_Estatus_Inactivo()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(client);
        var usuarioId = await SeedUsuarioConRolEnEmpresaAsync(EmpresaInicialId);
        await AsignarAsync(client, sucursalId, usuarioId);

        var response = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/usuarios/{usuarioId}/desactivar",
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
        var usuarioId = await SeedUsuarioConRolEnEmpresaAsync(EmpresaInicialId);
        await AsignarAsync(client, sucursalId, usuarioId);

        var desactivada = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/usuarios/{usuarioId}/desactivar",
            content: null);
        desactivada.EnsureSuccessStatusCode();

        var reactivada = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/usuarios/{usuarioId}/reactivar",
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
            $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/usuarios");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Listar_Sin_Permiso_Retorna_403()
    {
        var client = await CreateNoPermsClientAsync();
        var response = await client.GetAsync(
            $"{EmpresasBase}/sucursales/{Guid.NewGuid()}/usuarios");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Guard de pertenencia a sucursal (F1-ADM-01 Fase 2 sección C) ---

    [Fact]
    public async Task Listar_Con_Permiso_Leer_Pero_Sin_Pertenencia_Ni_Admin_Retorna_403()
    {
        var admin = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(admin);

        var (client, _) = await CreateUsuarioConPermisoAsync(
            "compartido.catalogos.leer");

        var response = await client.GetAsync($"{EmpresasBase}/sucursales/{sucursalId}/usuarios");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("SUCURSAL_NO_ASOCIADA", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Listar_Con_Pertenencia_A_La_Sucursal_Retorna_200()
    {
        var admin = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(admin);

        var (client, usuarioId) = await CreateUsuarioConPermisoAsync(
            "compartido.catalogos.leer");
        await SeedUsuarioSucursalAsync(usuarioId, sucursalId, EmpresaInicialId);

        var response = await client.GetAsync($"{EmpresasBase}/sucursales/{sucursalId}/usuarios");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Sucursales_De_Sesion_Muestran_Solo_Las_Asignadas_Al_Usuario()
    {
        var admin = await CreateSuperAdminClientAsync();
        var propia = await CrearSucursalAsync(admin, "SESP");
        var ajena = await CrearSucursalAsync(admin, "SESA");
        var (client, usuarioId) = await CreateUsuarioConPermisoAsync(
            "compartido.catalogos.leer");
        await SeedUsuarioSucursalAsync(usuarioId, propia, EmpresaInicialId);

        var response = await client.GetAsync("/api/auth/sucursales");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await ReadJsonAsync(response);
        Assert.Contains(items.EnumerateArray(), s => s.GetProperty("id").GetGuid() == propia);
        Assert.DoesNotContain(items.EnumerateArray(), s => s.GetProperty("id").GetGuid() == ajena);
    }

    [Fact]
    public async Task Catalogo_Empleados_No_Expone_Personal_De_Sucursal_Ajena()
    {
        var admin = await CreateSuperAdminClientAsync();
        var propia = await CrearSucursalAsync(admin, "EMP-P");
        var ajena = await CrearSucursalAsync(admin, "EMP-A");
        var (client, usuarioId) = await CreateUsuarioConPermisoAsync(
            "compartido.catalogos.leer");
        await SeedUsuarioSucursalAsync(usuarioId, propia, EmpresaInicialId);
        var clavePropia = $"EP-{Guid.NewGuid():N}"[..12];
        var claveAjena = $"EA-{Guid.NewGuid():N}"[..12];
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            db.Empleados.Add(new Empleado(Guid.CreateVersion7(), EmpresaInicialId, clavePropia, "Empleado propio", sucursalId: propia));
            db.Empleados.Add(new Empleado(Guid.CreateVersion7(), EmpresaInicialId, claveAjena, "Empleado ajeno", sucursalId: ajena));
            await db.SaveChangesAsync();
        }

        var listado = await client.GetAsync("/api/v1/catalogos/empleados?limit=200");
        Assert.Equal(HttpStatusCode.OK, listado.StatusCode);
        var items = (await ReadJsonAsync(listado)).GetProperty("items");
        Assert.Contains(items.EnumerateArray(), e => e.GetProperty("clave").GetString() == clavePropia);
        Assert.DoesNotContain(items.EnumerateArray(), e => e.GetProperty("clave").GetString() == claveAjena);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"/api/v1/catalogos/empleados?sucursalId={ajena}")).StatusCode);
    }

    [Fact]
    public async Task Listar_Con_Permiso_Admin_Sin_Pertenencia_Retorna_200()
    {
        var admin = await CreateSuperAdminClientAsync();
        var sucursalId = await CrearSucursalAsync(admin);

        var (client, _) = await CreateUsuarioConPermisoAsync(
            "compartido.catalogos.leer", "admin.sucursales.usuarios-gestionar");

        var response = await client.GetAsync($"{EmpresasBase}/sucursales/{sucursalId}/usuarios");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- Helpers ---

    private async Task<HttpClient> CreateNoPermsClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(
            client, SinPermisosOid, "noperms-sxu@test.local", "Sin Permisos SXU");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Crea un usuario fresco con un rol propio que trae los permisos
    /// indicados, asignado a <see cref="EmpresaInicialId"/>, y devuelve un
    /// client ya autenticado + el <c>UsuarioId</c> interno (para poder
    /// sembrar <c>UsuarioSucursal</c> después).
    /// </summary>
    private async Task<(HttpClient Client, Guid UsuarioId)> CreateUsuarioConPermisoAsync(
        params string[] permisoCodigos)
    {
        var random = Guid.NewGuid().ToString("N").Substring(0, 10).ToLowerInvariant();
        var oid = $"test-guard-{random}";
        Guid usuarioId;

        using (var scope = _factory.Services.CreateScope())
        {
            var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
            using var bypass = empresaContext.Bypass();

            var rolId = Guid.CreateVersion7();
            identidad.Roles.Add(new Rol(
                rolId, $"test-guard-rol-{random}", "Rol de prueba del guard de sucursal"));

            foreach (var codigo in permisoCodigos)
            {
                var permisoId = await identidad.Permisos.AsNoTracking()
                    .Where(p => p.Codigo == codigo)
                    .Select(p => p.Id)
                    .SingleAsync();
                identidad.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rolId, permisoId));
            }

            usuarioId = Guid.CreateVersion7();
            var usuario = new Usuario(usuarioId, oid, $"{oid}@test.local", "Usuario Guard Test");
            identidad.Usuarios.Add(usuario);
            identidad.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId));
            identidad.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(
                Guid.CreateVersion7(), usuarioId, EmpresaInicialId, rolId, asignadoPorUsuarioId: null));

            await identidad.SaveChangesAsync();
        }

        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, oid, $"{oid}@test.local", "Usuario Guard Test");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, usuarioId);
    }

    private async Task SeedUsuarioSucursalAsync(Guid usuarioId, Guid sucursalId, Guid empresaId)
    {
        using var scope = _factory.Services.CreateScope();
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        identidad.UsuarioSucursales.Add(new UsuarioSucursal(
            Guid.CreateVersion7(), usuarioId, sucursalId, empresaId));
        await identidad.SaveChangesAsync();
    }

    private async Task<Guid> SeedUsuarioConRolEnEmpresaAsync(Guid empresaId)
    {
        using var scope = _factory.Services.CreateScope();
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        var random = Guid.NewGuid().ToString("N").Substring(0, 10).ToLowerInvariant();
        var rolId = Guid.CreateVersion7();
        identidad.Roles.Add(new Rol(
            rolId, $"test-sxu-{random}", "Rol de prueba de asignación a sucursal"));

        var oid = $"test-sxu-{random}";
        var usuario = new Usuario(Guid.CreateVersion7(), oid, $"{oid}@test.local", "Usuario Sucursal Test");
        identidad.Usuarios.Add(usuario);
        identidad.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuario.Id));
        identidad.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(
            Guid.CreateVersion7(), usuario.Id, empresaId, rolId, asignadoPorUsuarioId: null));

        await identidad.SaveChangesAsync();
        return usuario.Id;
    }

    private static async Task<Guid> CrearSucursalAsync(HttpClient client, string prefix = "SXU")
    {
        var hex = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        var clave = $"{prefix}-{hex}";
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

    private static async Task AsignarAsync(HttpClient client, Guid sucursalId, Guid usuarioId)
    {
        var response = await client.PostAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/usuarios/{usuarioId}",
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
