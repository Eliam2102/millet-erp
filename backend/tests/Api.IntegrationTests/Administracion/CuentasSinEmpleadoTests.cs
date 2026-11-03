using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using static Millet.Api.IntegrationTests.Administracion.ColaboradoresEndpointsTests;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Empleados como único lugar para administrar a las personas (86e3x6cpw):
/// cuentas Microsoft sin empleado (contrato 1), empleado por cuenta
/// (contrato 2) y grupo de acceso de Entra al dar, quitar y devolver el
/// acceso (B2, contratos 3 y 4). El host configura
/// <c>Entra:GrupoAccesoErpId</c> sobre el directorio simulado.
/// </summary>
public class CuentasSinEmpleadoTests : IClassFixture<CuentasSinEmpleadoTests.HostGrupoAcceso>
{
    private const string Grupo = "grupo-erp-pruebas";
    private const string CuentasSinEmpleado = ColaboradoresEndpoint + "/cuentas-sin-empleado";
    private static readonly Guid EmpresaInicialId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    private readonly HostGrupoAcceso _host;

    public CuentasSinEmpleadoTests(HostGrupoAcceso host) => _host = host;

    private IEntraDirectorioPort Directorio => _host.Factory.Services.GetRequiredService<IEntraDirectorioPort>();

    [Fact]
    public async Task Lista_Cuentas_Del_Directorio_Sin_Empleado_Y_Las_Quita_Al_Ligarlas()
    {
        var admin = await CreateSuperAdminClientAsync();
        var cuenta = await CrearCuentaAsync("María Sin Ficha");

        var antes = await ListarAsync(admin, cuenta.Upn);
        var item = Assert.Single(antes.GetProperty("items").EnumerateArray());
        Assert.Equal(cuenta.ObjectId, item.GetProperty("objectId").GetString());
        Assert.Equal(cuenta.Upn, item.GetProperty("correo").GetString());
        Assert.Equal("María Sin Ficha", item.GetProperty("nombre").GetString());
        Assert.True(item.GetProperty("habilitada").GetBoolean());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("usuarioId").ValueKind);
        Assert.False(item.GetProperty("yaEntroAlErp").GetBoolean());
        Assert.Equal(1, antes.GetProperty("total").GetInt32());

        var org = await CrearOrganizacionAsync(admin);
        var alta = await PostAltaAsync(admin, org, acceso: 1, cuenta.Upn, org.RolId);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        var despues = await ListarAsync(admin, cuenta.Upn);
        Assert.Empty(despues.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Usuario_Que_Entro_Sin_Empleado_Aparece_Como_Ya_Entro()
    {
        var admin = await CreateSuperAdminClientAsync();
        var oid = $"jit-{RandomSufijo()}";
        var correo = $"{oid}@test.local";
        await FakeLoginAsync(_host.Factory.CreateClientWithIdempotency(), oid, correo, "Entró Sin Ficha");

        var body = await ListarAsync(admin, correo);
        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(oid, item.GetProperty("objectId").GetString());
        Assert.NotEqual(JsonValueKind.Null, item.GetProperty("usuarioId").ValueKind);
        Assert.True(item.GetProperty("yaEntroAlErp").GetBoolean());
    }

    [Fact]
    public async Task Operativo_Y_Sin_Permiso_Reciben_403()
    {
        var operativo = await CreateUsuarioConPermisosAsync(sucursalOperativa: true,
            PermisosCanonicos.AdminEmpleadosGestionar, PermisosCanonicos.IdentidadUsuariosCrear);
        Assert.Equal(HttpStatusCode.Forbidden, (await operativo.GetAsync(CuentasSinEmpleado)).StatusCode);

        var sinCrearUsuarios = await CreateUsuarioConPermisosAsync(sucursalOperativa: false,
            PermisosCanonicos.AdminEmpleadosGestionar, PermisosCanonicos.AdminEmpleadosGestionarTodasSucursales);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinCrearUsuarios.GetAsync(CuentasSinEmpleado)).StatusCode);
    }

    [Fact]
    public async Task Por_Usuario_Devuelve_El_Empleado_O_404()
    {
        var admin = await CreateSuperAdminClientAsync();
        var (empleadoId, usuarioId, _) = await AltaConCuentaExistenteAsync(admin);

        var ok = await admin.GetAsync($"{ColaboradoresEndpoint}/por-usuario/{usuarioId}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(empleadoId, (await ReadJsonAsync(ok)).GetProperty("empleadoId").GetGuid());

        var sinEmpleado = await admin.GetAsync($"{ColaboradoresEndpoint}/por-usuario/{Guid.NewGuid()}");
        await AssertProblemAsync(sinEmpleado, HttpStatusCode.NotFound, "EMPLEADO_NO_ENCONTRADO");
    }

    [Fact]
    public async Task Dar_Acceso_Agrega_Al_Grupo_Desactivar_Lo_Quita_Y_Reactivar_Lo_Devuelve()
    {
        var admin = await CreateSuperAdminClientAsync();
        var (empleadoId, usuarioId, oid) = await AltaConCuentaExistenteAsync(admin);

        Assert.True(await Directorio.EsMiembroDeGrupoAccesoAsync(Grupo, oid, default));
        Assert.Equal("Sincronizado", await GrupoAccesoAsync(admin, empleadoId));

        (await admin.PostAsync($"/api/v1/identidad/usuarios/{usuarioId}/desactivar", null)).EnsureSuccessStatusCode();
        Assert.False(await Directorio.EsMiembroDeGrupoAccesoAsync(Grupo, oid, default));
        Assert.Equal("Sincronizado", await GrupoAccesoAsync(admin, empleadoId));

        (await admin.PostAsync($"/api/v1/identidad/usuarios/{usuarioId}/reactivar", null)).EnsureSuccessStatusCode();
        Assert.True(await Directorio.EsMiembroDeGrupoAccesoAsync(Grupo, oid, default));
    }

    [Fact]
    public async Task Baja_Del_Colaborador_Lo_Quita_Del_Grupo()
    {
        var admin = await CreateSuperAdminClientAsync();
        var (empleadoId, _, oid) = await AltaConCuentaExistenteAsync(admin);

        (await admin.PostAsync($"/api/v1/admin/empleados/{empleadoId}/desactivar", null)).EnsureSuccessStatusCode();

        Assert.False(await Directorio.EsMiembroDeGrupoAccesoAsync(Grupo, oid, default));
    }

    [Fact]
    public async Task Membresia_Fuera_De_Sincronia_Se_Reporta_Pendiente()
    {
        var admin = await CreateSuperAdminClientAsync();
        var (empleadoId, _, oid) = await AltaConCuentaExistenteAsync(admin);

        // Alguien sacó la cuenta del grupo por fuera del ERP (o falló Graph).
        await Directorio.QuitarDeGrupoAccesoAsync(Grupo, oid, default);

        Assert.Equal("Pendiente", await GrupoAccesoAsync(admin, empleadoId));
    }

    // --- Fix PR #270: la cuenta existente se liga sin exigir dominio permitido ---

    [Fact]
    public async Task Crear_Empleado_Con_Cuenta_Que_Ya_Entro_De_Otro_Dominio_La_Liga()
    {
        var admin = await CreateSuperAdminClientAsync();
        var (correo, usuarioId) = await EntrarSinEmpleadoAsync("gmail.com");
        var org = await CrearOrganizacionAsync(admin);

        var alta = await PostAltaAsync(admin, org, acceso: 1, correo, org.RolId);

        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var acceso = (await ReadJsonAsync(alta)).GetProperty("acceso");
        Assert.Equal(usuarioId, acceso.GetProperty("usuarioId").GetGuid());
        Assert.True(acceso.GetProperty("usuarioReutilizado").GetBoolean());
    }

    [Fact]
    public async Task Ligar_Empleado_Existente_A_Cuenta_De_Otro_Dominio_Funciona()
    {
        var admin = await CreateSuperAdminClientAsync();
        var (correo, usuarioId) = await EntrarSinEmpleadoAsync("vilostudio.ai");
        var org = await CrearOrganizacionAsync(admin);
        var sinAcceso = await PostAltaAsync(admin, org, acceso: 0, correo: null, rolId: null);
        var empleadoId = (await ReadJsonAsync(sinAcceso)).GetProperty("empleado").GetProperty("id").GetGuid();

        var response = await admin.PostAsJsonAsync($"{ColaboradoresEndpoint}/{empleadoId}/acceso",
            new { Acceso = 1, CorreoCorporativo = correo, RolId = org.RolId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(usuarioId, (await ReadJsonAsync(response)).GetProperty("acceso").GetProperty("usuarioId").GetGuid());
    }

    [Fact]
    public async Task Cuenta_Nueva_Con_Dominio_No_Permitido_Retorna_422()
    {
        var admin = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(admin);

        var alta = await PostAltaAsync(admin, org, acceso: 2, $"nueva-{RandomSufijo()}@gmail.com", org.RolId);

        await AssertProblemAsync(alta, HttpStatusCode.UnprocessableEntity, "ENTRA_DOMINIO_NO_PERMITIDO");
    }

    // --- Helpers ---

    /// <summary>Cuenta de otro dominio que entra al ERP sin empleado (JIT, como una invitada).</summary>
    private async Task<(string Correo, Guid UsuarioId)> EntrarSinEmpleadoAsync(string dominio)
    {
        var oid = Guid.NewGuid().ToString();
        var correo = $"invitada-{RandomSufijo()}@{dominio}";
        await FakeLoginAsync(_host.Factory.CreateClientWithIdempotency(), oid, correo, "Cuenta Invitada");
        using var scope = _host.Factory.Services.CreateScope();
        var usuarioId = await scope.ServiceProvider.GetRequiredService<IdentidadDbContext>().Usuarios
            .IgnoreQueryFilters().Where(u => u.EntraOid == oid).Select(u => u.Id).SingleAsync();
        return (correo, usuarioId);
    }

    private async Task<CuentaEntra> CrearCuentaAsync(string nombre)
    {
        var upn = $"sinficha-{RandomSufijo()}@millet.mx";
        return (await Directorio.CrearCuentaAsync(
            new SolicitudCuentaEntra(upn, nombre, "contacto@gmail.com", upn), default)).Cuenta;
    }

    private async Task<(Guid EmpleadoId, Guid UsuarioId, string Oid)> AltaConCuentaExistenteAsync(HttpClient admin)
    {
        var cuenta = await CrearCuentaAsync("Colaborador Con Grupo");
        var org = await CrearOrganizacionAsync(admin);
        var alta = await PostAltaAsync(admin, org, acceso: 1, cuenta.Upn, org.RolId);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var body = await ReadJsonAsync(alta);
        return (body.GetProperty("empleado").GetProperty("id").GetGuid(),
            body.GetProperty("acceso").GetProperty("usuarioId").GetGuid(), cuenta.ObjectId);
    }

    private static async Task<JsonElement> ListarAsync(HttpClient client, string buscar)
    {
        var response = await client.GetAsync($"{CuentasSinEmpleado}?buscar={Uri.EscapeDataString(buscar)}&limit=50");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static async Task<string?> GrupoAccesoAsync(HttpClient client, Guid empleadoId)
    {
        var response = await client.GetAsync($"{ColaboradoresEndpoint}/{empleadoId}/acceso");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("grupoAcceso").GetString();
    }

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _host.Factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<HttpClient> CreateUsuarioConPermisosAsync(bool sucursalOperativa, params string[] permisoCodigos)
    {
        var random = RandomSufijo();
        var oid = $"test-sin-empleado-{random}";
        var usuarioId = Guid.CreateVersion7();
        using (var scope = _host.Factory.Services.CreateScope())
        {
            var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var rolId = Guid.CreateVersion7();
            identidad.Roles.Add(new Rol(rolId, $"test-sin-empleado-{random}", "Rol de prueba cuentas sin empleado"));
            foreach (var codigo in permisoCodigos)
            {
                var permisoId = await identidad.Permisos.AsNoTracking()
                    .Where(p => p.Codigo == codigo).Select(p => p.Id).SingleAsync();
                identidad.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rolId, permisoId));
            }
            identidad.Usuarios.Add(new Usuario(usuarioId, oid, $"{oid}@test.local", "Usuario Sin Empleado"));
            identidad.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId));
            identidad.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(
                Guid.CreateVersion7(), usuarioId, EmpresaInicialId, rolId, asignadoPorUsuarioId: null));
            if (sucursalOperativa)
            {
                var org = await CrearOrganizacionAsync(await CreateSuperAdminClientAsync());
                identidad.UsuarioSucursales.Add(new UsuarioSucursal(
                    Guid.CreateVersion7(), usuarioId, org.SucursalId, EmpresaInicialId));
            }
            await identidad.SaveChangesAsync();
        }

        var client = _host.Factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, oid, $"{oid}@test.local", "Usuario Sin Empleado");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Host con el grupo de acceso configurado.</summary>
    public sealed class HostGrupoAcceso : IDisposable
    {
        public WebApplicationFactory<Program> Factory { get; } =
            new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
                b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Entra:GrupoAccesoErpId"] = Grupo })));

        public void Dispose() => Factory.Dispose();
    }
}

/// <summary>
/// «No hay acceso sin empleado» (86e3x6cpw, B3) con
/// <c>Identidad:ExigirEmpleadoParaAcceso=true</c>.
/// </summary>
public class LoginExigeEmpleadoTests : IClassFixture<LoginExigeEmpleadoTests.HostExigeEmpleado>
{
    private static readonly Guid EmpresaInicialId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    private readonly HostExigeEmpleado _host;

    public LoginExigeEmpleadoTests(HostExigeEmpleado host) => _host = host;

    [Fact]
    public async Task Cuenta_Sin_Empleado_Recibe_403_Usuario_Sin_Empleado()
    {
        var oid = $"sin-empleado-{RandomSufijo()}";

        var response = await FakeLoginRawAsync(oid);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "USUARIO_SIN_EMPLEADO");
        Assert.Contains("Pide al administrador que la ligue en Empleados", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cuenta_Con_Empleado_Activo_Entra_E_Inactivo_No()
    {
        var oid = $"con-empleado-{RandomSufijo()}";
        var empleadoId = await SembrarUsuarioConEmpleadoAsync(oid);

        Assert.Equal(HttpStatusCode.OK, (await FakeLoginRawAsync(oid)).StatusCode);

        using (var scope = _host.Factory.Services.CreateScope())
        {
            var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            var empleado = await compartido.Empleados.IgnoreQueryFilters().SingleAsync(e => e.Id == empleadoId);
            empleado.Desactivar();
            await compartido.SaveChangesAsync();
        }
        await AssertProblemAsync(await FakeLoginRawAsync(oid), HttpStatusCode.Forbidden, "USUARIO_SIN_EMPLEADO");
    }

    [Fact]
    public async Task Cuenta_Tecnica_Sin_Empleado_Entra()
    {
        var oid = $"tecnica-{RandomSufijo()}";
        using (var scope = _host.Factory.Services.CreateScope())
        {
            var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            var usuario = new Usuario(Guid.CreateVersion7(), oid, $"{oid}@test.local", "Cuenta Técnica", esCuentaTecnica: true);
            identidad.Usuarios.Add(usuario);
            identidad.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuario.Id));
            await identidad.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await FakeLoginRawAsync(oid)).StatusCode);
    }

    private async Task<Guid> SembrarUsuarioConEmpleadoAsync(string oid)
    {
        using var scope = _host.Factory.Services.CreateScope();
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var usuario = new Usuario(Guid.CreateVersion7(), oid, $"{oid}@test.local", "Con Empleado");
        identidad.Usuarios.Add(usuario);
        identidad.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuario.Id));
        await identidad.SaveChangesAsync();
        var empleado = new Empleado(Guid.CreateVersion7(), EmpresaInicialId, RandomClave("LEE"), "Con Empleado",
            usuarioId: usuario.Id);
        compartido.Empleados.Add(empleado);
        await compartido.SaveChangesAsync();
        return empleado.Id;
    }

    private Task<HttpResponseMessage> FakeLoginRawAsync(string oid) =>
        _host.Factory.CreateClientWithIdempotency().PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid,
            Email = $"{oid}@test.local",
            Nombre = "Prueba Login",
            EmpresaId = (Guid?)null,
        });

    /// <summary>Host con la regla «no hay acceso sin empleado» encendida.</summary>
    public sealed class HostExigeEmpleado : IDisposable
    {
        public WebApplicationFactory<Program> Factory { get; } =
            new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
                b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Identidad:ExigirEmpleadoParaAcceso"] = "true" })));

        public void Dispose() => Factory.Dispose();
    }
}
