using Millet.Identidad.Application.Roles;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Identidad.UnitTests.Infrastructure;

public sealed class GruposEntraP6Tests
{
    [Fact]
    public async Task Grupo_suma_rol_y_al_salir_pierde_sus_permisos_sin_borrar_rol_manual()
    {
        var s = new PermisosOverrideScenario();
        var manual = await s.CrearRolAsync("manual", false, PermisosCanonicos.IdentidadUsuariosLeer);
        var grupo = await s.CrearRolAsync("grupo", false, PermisosCanonicos.IdentidadUsuariosCrear);
        var usuario = await s.CrearUsuarioAsync("p6", PermisosOverrideScenario.EmpresaA, manual);
        s.Db.RolGruposEntraId.Add(new RolGrupoEntraId(Guid.NewGuid(), grupo.Id, "GRUPO-ENTRA", "Prueba"));
        s.Db.UsuarioGruposEntraId.Add(new UsuarioGrupoEntraId(usuario.Id, "grupo-entra"));
        await s.Db.SaveChangesAsync();
        (await s.Loader.LoadForUserInEmpresaAsync(usuario.Id, PermisosOverrideScenario.EmpresaA))
            .Should().Contain(PermisosCanonicos.IdentidadUsuariosCrear).And.Contain(PermisosCanonicos.IdentidadUsuariosLeer);
        s.Db.UsuarioGruposEntraId.RemoveRange(s.Db.UsuarioGruposEntraId);
        await s.Db.SaveChangesAsync();
        (await s.Loader.LoadForUserInEmpresaAsync(usuario.Id, PermisosOverrideScenario.EmpresaA))
            .Should().NotContain(PermisosCanonicos.IdentidadUsuariosCrear).And.Contain(PermisosCanonicos.IdentidadUsuariosLeer);
        (await s.Loader.LoadForUserInEmpresaAsync(usuario.Id, PermisosOverrideScenario.EmpresaB)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Desactivar_rol_invalida_cache_de_asignacion_directa_y_por_grupo(bool porGrupo)
    {
        var s = new PermisosOverrideScenario();
        var manual = await s.CrearRolAsync("base", false, PermisosCanonicos.IdentidadUsuariosLeer);
        var rol = await s.CrearRolAsync("inactivar", false, PermisosCanonicos.IdentidadUsuariosCrear);
        var usuario = await s.CrearUsuarioAsync("p6", PermisosOverrideScenario.EmpresaA, porGrupo ? manual : rol);
        if (porGrupo)
        {
            s.Db.RolGruposEntraId.Add(new RolGrupoEntraId(Guid.NewGuid(), rol.Id, "grupo", "Prueba"));
            s.Db.UsuarioGruposEntraId.Add(new UsuarioGrupoEntraId(usuario.Id, "grupo"));
            await s.Db.SaveChangesAsync();
        }
        var cache = new InMemoryPermissionCache(s.Clock);
        await cache.SetAsync(usuario.Id, PermisosOverrideScenario.EmpresaA,
            await s.Loader.LoadForUserInEmpresaAsync(usuario.Id, PermisosOverrideScenario.EmpresaA));
        await new EliminarRolHandler(s.Db, cache).Handle(new EliminarRolCommand(rol.Id), CancellationToken.None);
        (await cache.GetAsync(usuario.Id, PermisosOverrideScenario.EmpresaA)).Should().BeNull();
        (await s.Loader.LoadForUserInEmpresaAsync(usuario.Id, PermisosOverrideScenario.EmpresaA))
            .Should().NotContain(PermisosCanonicos.IdentidadUsuariosCrear);
    }

    [Theory]
    [InlineData("admin-organizacional", false)]
    [InlineData("rol-personalizado", false)]
    [InlineData("super-admin", true)]
    public async Task Crear_y_desactivar_empresas_solo_pertenecen_al_super_admin(string codigo, bool autorizado)
    {
        var s = new PermisosOverrideScenario();
        var rol = await s.CrearRolAsync(codigo, false, PermisosCanonicos.AdminEmpresasCrear, PermisosCanonicos.AdminEmpresasDesactivar);
        var usuario = await s.CrearUsuarioAsync("p6", PermisosOverrideScenario.EmpresaA, rol);
        await s.AgregarOverrideAsync(usuario.Id, PermisosOverrideScenario.EmpresaA,
            PermisosCanonicos.AdminEmpresasCrear, EfectoPermiso.Conceder);
        var permisos = await s.Loader.LoadForUserInEmpresaAsync(usuario.Id, PermisosOverrideScenario.EmpresaA);
        permisos.Contains(PermisosCanonicos.AdminEmpresasCrear).Should().Be(autorizado);
        permisos.Contains(PermisosCanonicos.AdminEmpresasDesactivar).Should().Be(autorizado);
    }
}
