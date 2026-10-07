using Millet.Identidad.Domain;

namespace Millet.Identidad.UnitTests.Infrastructure;

/// <summary>
/// <see cref="Millet.Identidad.Infrastructure.PermissionLoader"/> con excepciones
/// por usuario (ADR-0053): efectivos = (rol ∪ Conceder) \ Denegar.
/// </summary>
public sealed class PermissionLoaderOverrideTests
{
    private const string Leer = PermisosCanonicos.IdentidadUsuariosLeer;
    private const string Crear = PermisosCanonicos.IdentidadUsuariosCrear;
    private const string Editar = PermisosCanonicos.IdentidadUsuariosEditar;
    private const string Roles = PermisosCanonicos.IdentidadRolesLeer;
    private static readonly Guid A = PermisosOverrideScenario.EmpresaA;
    private static readonly Guid B = PermisosOverrideScenario.EmpresaB;

    [Fact]
    public async Task Sin_overrides_devuelve_exactamente_los_permisos_del_rol()
    {
        var s = new PermisosOverrideScenario();
        var rol = await s.CrearRolAsync("operador", false, Leer, Crear);
        var u = await s.CrearUsuarioAsync("u1", A, rol);

        var permisos = await s.Loader.LoadForUserInEmpresaAsync(u.Id, A);

        permisos.Should().BeEquivalentTo(new[] { Leer, Crear });
    }

    [Fact]
    public async Task Conceder_anade_un_permiso_que_el_rol_no_trae()
    {
        var s = new PermisosOverrideScenario();
        var rol = await s.CrearRolAsync("operador", false, Leer);
        var u = await s.CrearUsuarioAsync("u1", A, rol);
        await s.AgregarOverrideAsync(u.Id, A, Editar, EfectoPermiso.Conceder);

        var permisos = await s.Loader.LoadForUserInEmpresaAsync(u.Id, A);

        permisos.Should().BeEquivalentTo(new[] { Leer, Editar });
    }

    [Fact]
    public async Task Denegar_quita_un_permiso_que_el_rol_trae()
    {
        var s = new PermisosOverrideScenario();
        var rol = await s.CrearRolAsync("operador", false, Leer, Crear);
        var u = await s.CrearUsuarioAsync("u1", A, rol);
        await s.AgregarOverrideAsync(u.Id, A, Crear, EfectoPermiso.Denegar);

        var permisos = await s.Loader.LoadForUserInEmpresaAsync(u.Id, A);

        permisos.Should().BeEquivalentTo(new[] { Leer });
    }

    [Fact]
    public async Task Conceder_y_denegar_combinados()
    {
        var s = new PermisosOverrideScenario();
        var rol = await s.CrearRolAsync("operador", false, Leer, Crear);
        var u = await s.CrearUsuarioAsync("u1", A, rol);
        await s.AgregarOverrideAsync(u.Id, A, Editar, EfectoPermiso.Conceder);
        await s.AgregarOverrideAsync(u.Id, A, Crear, EfectoPermiso.Denegar);

        var permisos = await s.Loader.LoadForUserInEmpresaAsync(u.Id, A);

        permisos.Should().BeEquivalentTo(new[] { Leer, Editar });
    }

    [Fact]
    public async Task Dos_usuarios_con_el_mismo_rol_obtienen_permisos_distintos()
    {
        var s = new PermisosOverrideScenario();
        var rol = await s.CrearRolAsync("operador", false, Leer, Crear);
        var u1 = await s.CrearUsuarioAsync("u1", A, rol);
        var u2 = await s.CrearUsuarioAsync("u2", A, rol);
        await s.AgregarOverrideAsync(u1.Id, A, Crear, EfectoPermiso.Denegar);
        await s.AgregarOverrideAsync(u2.Id, A, Roles, EfectoPermiso.Conceder);

        (await s.Loader.LoadForUserInEmpresaAsync(u1.Id, A)).Should().BeEquivalentTo(new[] { Leer });
        (await s.Loader.LoadForUserInEmpresaAsync(u2.Id, A)).Should().BeEquivalentTo(new[] { Leer, Crear, Roles });
    }

    [Fact]
    public async Task Rol_inactivo_no_da_nada_ni_siquiera_los_concedidos()
    {
        var s = new PermisosOverrideScenario();
        var rol = await s.CrearRolAsync("operador", false, Leer);
        var u = await s.CrearUsuarioAsync("u1", A, rol);
        await s.AgregarOverrideAsync(u.Id, A, Editar, EfectoPermiso.Conceder);
        rol.Desactivar();
        await s.Db.SaveChangesAsync();

        var permisos = await s.Loader.LoadForUserInEmpresaAsync(u.Id, A);

        permisos.Should().BeEmpty();
    }

    [Fact]
    public async Task Usuario_inactivo_no_tiene_permisos()
    {
        var s = new PermisosOverrideScenario();
        var rol = await s.CrearRolAsync("operador", false, Leer);
        var u = await s.CrearUsuarioAsync("u1", A, rol);
        await s.AgregarOverrideAsync(u.Id, A, Editar, EfectoPermiso.Conceder);
        u.Desactivar();
        await s.Db.SaveChangesAsync();

        (await s.Loader.LoadForUserInEmpresaAsync(u.Id, A)).Should().BeEmpty();
    }

    [Fact]
    public async Task Override_de_otra_empresa_no_se_mezcla()
    {
        var s = new PermisosOverrideScenario();
        var rol = await s.CrearRolAsync("operador", false, Leer, Crear);
        var u = await s.CrearUsuarioAsync("u1", A, rol);
        s.Db.UsuarioEmpresaRoles.Add(u.AsignarRolEnEmpresa(B, rol.Id, null));
        await s.Db.SaveChangesAsync();
        await s.AgregarOverrideAsync(u.Id, B, Crear, EfectoPermiso.Denegar);
        await s.AgregarOverrideAsync(u.Id, B, Editar, EfectoPermiso.Conceder);

        (await s.Loader.LoadForUserInEmpresaAsync(u.Id, A)).Should().BeEquivalentTo(new[] { Leer, Crear });
        (await s.Loader.LoadForUserInEmpresaAsync(u.Id, B)).Should().BeEquivalentTo(new[] { Leer, Editar });
    }
}
