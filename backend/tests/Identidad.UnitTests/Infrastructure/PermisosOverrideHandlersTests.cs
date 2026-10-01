using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Application.Events;
using Millet.Identidad.Application.Usuarios;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.UnitTests.Infrastructure;

/// <summary>
/// Handlers de permisos personalizados (ADR-0053): reglas de seguridad del PUT,
/// restablecer y borrado por cambio de rol (asignar/revocar).
/// </summary>
public sealed class PermisosOverrideHandlersTests
{
    private const string Leer = PermisosCanonicos.IdentidadUsuariosLeer;
    private const string Crear = PermisosCanonicos.IdentidadUsuariosCrear;
    private const string Editar = PermisosCanonicos.IdentidadUsuariosEditar;
    private const string Gestionar = PermisosCanonicos.IdentidadUsuariosGestionarPermisos;
    private static readonly Guid A = PermisosOverrideScenario.EmpresaA;

    private static PermisoOverrideItem Item(string codigo, string efecto, string? motivo = null) =>
        new(PermisosOverrideScenario.PermisoId(codigo), efecto, motivo);

    private static ActualizarPermisosOverrideUsuarioHandler Put(PermisosOverrideScenario s) =>
        new(s.Db, s.Events, s.CurrentUser, s.EmpresaContext, s.Loader, s.Cache, s.Clock);

    /// <summary>Caller admin (gestionar + leer + crear) y target operador (leer + crear) en la empresa A.</summary>
    private static async Task<(Usuario Caller, Usuario Target)> EscenarioAsync(PermisosOverrideScenario s)
    {
        var rolAdmin = await s.CrearRolAsync("admin", false, Gestionar, Leer, Crear);
        var rolOp = await s.CrearRolAsync("operador", false, Leer, Crear);
        var caller = await s.CrearUsuarioAsync("caller", A, rolAdmin);
        var target = await s.CrearUsuarioAsync("target", A, rolOp);
        s.CurrentUser.UserId = caller.Id;
        return (caller, target);
    }

    [Fact]
    public async Task Put_guarda_excepciones_invalida_cache_y_publica_evento_sin_secretos()
    {
        var s = new PermisosOverrideScenario();
        var (_, target) = await EscenarioAsync(s);

        var resp = await Put(s).Handle(
            new ActualizarPermisosOverrideUsuarioCommand(target.Id, A, new[]
            {
                Item(Crear, "Denegar", "ya no captura"),
                Item(Gestionar, "Conceder"),
            }),
            CancellationToken.None);

        resp.Concedidos.Should().Be(1);
        resp.Denegados.Should().Be(1);
        (await s.Loader.LoadForUserInEmpresaAsync(target.Id, A))
            .Should().BeEquivalentTo(new[] { Leer, Gestionar });
        s.Cache.Invalidaciones.Should().Contain((target.Id, A));
        var evento = s.Events.Publicados.Should().ContainSingle().Subject
            .Should().BeOfType<UsuarioPermisosOverrideActualizadosEvent>().Subject;
        evento.Concedidos.Should().Be(1);
        evento.Denegados.Should().Be(1);
    }

    [Fact]
    public async Task Put_reemplaza_la_lista_anterior()
    {
        var s = new PermisosOverrideScenario();
        var (_, target) = await EscenarioAsync(s);
        await Put(s).Handle(new ActualizarPermisosOverrideUsuarioCommand(
            target.Id, A, new[] { Item(Crear, "Denegar") }), CancellationToken.None);

        await Put(s).Handle(new ActualizarPermisosOverrideUsuarioCommand(
            target.Id, A, new[] { Item(Gestionar, "Conceder") }), CancellationToken.None);

        (await s.Loader.LoadForUserInEmpresaAsync(target.Id, A))
            .Should().BeEquivalentTo(new[] { Leer, Crear, Gestionar });
        (await s.Db.UsuarioPermisoOverrides.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Put_rechaza_escalada_de_privilegios()
    {
        var s = new PermisosOverrideScenario();
        var (_, target) = await EscenarioAsync(s);

        // El caller (admin) no posee identidad.usuarios.editar.
        var act = () => Put(s).Handle(new ActualizarPermisosOverrideUsuarioCommand(
            target.Id, A, new[] { Item(Editar, "Conceder") }), CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be("PERMISOS_OVERRIDE_ESCALADA");
        (await s.Db.UsuarioPermisoOverrides.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Put_permite_denegar_aunque_el_caller_no_posea_el_permiso()
    {
        var s = new PermisosOverrideScenario();
        var rolOp = await s.CrearRolAsync("operador", false, Leer, Editar);
        var rolCaller = await s.CrearRolAsync("rrhh", false, Gestionar, Leer);
        var caller = await s.CrearUsuarioAsync("caller", A, rolCaller);
        var target = await s.CrearUsuarioAsync("target", A, rolOp);
        s.CurrentUser.UserId = caller.Id;

        await Put(s).Handle(new ActualizarPermisosOverrideUsuarioCommand(
            target.Id, A, new[] { Item(Editar, "Denegar") }), CancellationToken.None);

        (await s.Loader.LoadForUserInEmpresaAsync(target.Id, A)).Should().BeEquivalentTo(new[] { Leer });
    }

    [Fact]
    public async Task Put_rechaza_auto_edicion()
    {
        var s = new PermisosOverrideScenario();
        var (caller, _) = await EscenarioAsync(s);

        var act = () => Put(s).Handle(new ActualizarPermisosOverrideUsuarioCommand(
            caller.Id, A, new[] { Item(Crear, "Denegar") }), CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be("PERMISOS_OVERRIDE_AUTO_EDICION");
    }

    [Fact]
    public async Task Put_rechaza_si_el_caller_no_gestiona_permisos_en_esa_empresa()
    {
        var s = new PermisosOverrideScenario();
        var rolOp = await s.CrearRolAsync("operador", false, Leer);
        var caller = await s.CrearUsuarioAsync("caller", A, rolOp);
        var target = await s.CrearUsuarioAsync("target", A, rolOp);
        s.CurrentUser.UserId = caller.Id;

        var act = () => Put(s).Handle(new ActualizarPermisosOverrideUsuarioCommand(
            target.Id, A, new[] { Item(Leer, "Denegar") }), CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be("PERMISOS_OVERRIDE_SIN_ALCANCE_EMPRESA");
    }

    [Fact]
    public async Task Put_protege_al_super_admin()
    {
        var s = new PermisosOverrideScenario();
        var (_, _) = await EscenarioAsync(s);
        var rolSuper = await s.CrearRolAsync("super-admin", true, Leer);
        var superAdmin = await s.CrearUsuarioAsync("super", A, rolSuper);

        var act = () => Put(s).Handle(new ActualizarPermisosOverrideUsuarioCommand(
            superAdmin.Id, A, new[] { Item(Leer, "Denegar") }), CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("SUPER_ADMIN_PERMISOS_PROTEGIDOS");
    }

    [Fact]
    public async Task Put_permite_editar_a_un_usuario_con_rol_de_sistema_que_no_es_super_admin()
    {
        var s = new PermisosOverrideScenario();
        var (_, _) = await EscenarioAsync(s);
        var rolSistema = await s.CrearRolAsync("admin-catalogos", true, Leer, Crear);
        var target = await s.CrearUsuarioAsync("catalogos", A, rolSistema);

        await Put(s).Handle(new ActualizarPermisosOverrideUsuarioCommand(
            target.Id, A, new[] { Item(Crear, "Denegar") }), CancellationToken.None);

        (await s.Loader.LoadForUserInEmpresaAsync(target.Id, A)).Should().BeEquivalentTo(new[] { Leer });
        var get = new ObtenerPermisosEfectivosUsuarioHandler(s.Db, s.EmpresaContext);
        (await get.Handle(new ObtenerPermisosEfectivosUsuarioQuery(target.Id, A), CancellationToken.None))
            .RolEsSuperAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task Get_marca_RolEsSuperAdmin_solo_para_el_rol_super_admin()
    {
        var s = new PermisosOverrideScenario();
        var rolSuper = await s.CrearRolAsync("super-admin", true, Leer);
        var superAdmin = await s.CrearUsuarioAsync("super", A, rolSuper);
        var get = new ObtenerPermisosEfectivosUsuarioHandler(s.Db, s.EmpresaContext);

        (await get.Handle(new ObtenerPermisosEfectivosUsuarioQuery(superAdmin.Id, A), CancellationToken.None))
            .RolEsSuperAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task Put_rechaza_permiso_inexistente()
    {
        var s = new PermisosOverrideScenario();
        var (_, target) = await EscenarioAsync(s);

        var act = () => Put(s).Handle(new ActualizarPermisosOverrideUsuarioCommand(
            target.Id, A, new[] { new PermisoOverrideItem(Guid.NewGuid(), "Denegar", null) }),
            CancellationToken.None);

        (await act.Should().ThrowAsync<EntityNotFoundException>())
            .Which.Code.Should().Be("PERMISO_NO_ENCONTRADO");
    }

    [Fact]
    public async Task Put_rechaza_usuario_sin_rol_en_la_empresa_o_inactivo()
    {
        var s = new PermisosOverrideScenario();
        var (_, target) = await EscenarioAsync(s);
        var sinRol = await s.CrearUsuarioAsync("sinrol");

        var actSinRol = () => Put(s).Handle(new ActualizarPermisosOverrideUsuarioCommand(
            sinRol.Id, A, new[] { Item(Leer, "Denegar") }), CancellationToken.None);
        (await actSinRol.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("USUARIO_SIN_ROL_EN_EMPRESA");

        var u = await s.Db.Usuarios.FirstAsync(x => x.Id == target.Id);
        u.Desactivar();
        await s.Db.SaveChangesAsync();
        var actInactivo = () => Put(s).Handle(new ActualizarPermisosOverrideUsuarioCommand(
            target.Id, A, new[] { Item(Leer, "Denegar") }), CancellationToken.None);
        (await actInactivo.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("USUARIO_INACTIVO");
    }

    [Fact]
    public void Validator_rechaza_duplicados_ambos_efectos_y_efecto_invalido()
    {
        var v = new ActualizarPermisosOverrideUsuarioValidator();
        var id = Guid.NewGuid();
        var u = Guid.NewGuid();
        var e = Guid.NewGuid();

        v.Validate(new ActualizarPermisosOverrideUsuarioCommand(u, e, new[]
        {
            new PermisoOverrideItem(id, "Conceder", null),
            new PermisoOverrideItem(id, "Conceder", null),
        })).IsValid.Should().BeFalse();

        v.Validate(new ActualizarPermisosOverrideUsuarioCommand(u, e, new[]
        {
            new PermisoOverrideItem(id, "Conceder", null),
            new PermisoOverrideItem(id, "Denegar", null),
        })).IsValid.Should().BeFalse();

        v.Validate(new ActualizarPermisosOverrideUsuarioCommand(u, e, new[]
        {
            new PermisoOverrideItem(id, "Otro", null),
        })).IsValid.Should().BeFalse();

        v.Validate(new ActualizarPermisosOverrideUsuarioCommand(u, e, new[]
        {
            new PermisoOverrideItem(id, "denegar", "ok"),
        })).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Restablecer_borra_todo_vuelve_al_rol_y_es_idempotente()
    {
        var s = new PermisosOverrideScenario();
        var (_, target) = await EscenarioAsync(s);
        await Put(s).Handle(new ActualizarPermisosOverrideUsuarioCommand(
            target.Id, A, new[] { Item(Crear, "Denegar") }), CancellationToken.None);
        s.Events.Publicados.Clear();
        var handler = new RestablecerPermisosOverrideUsuarioHandler(
            s.Db, s.Events, s.CurrentUser, s.EmpresaContext, s.Loader, s.Cache, s.Clock);

        await handler.Handle(new RestablecerPermisosOverrideUsuarioCommand(target.Id, A), CancellationToken.None);

        (await s.Loader.LoadForUserInEmpresaAsync(target.Id, A)).Should().BeEquivalentTo(new[] { Leer, Crear });
        s.Events.Publicados.Should().ContainSingle();

        s.Events.Publicados.Clear();
        await handler.Handle(new RestablecerPermisosOverrideUsuarioCommand(target.Id, A), CancellationToken.None);
        s.Events.Publicados.Should().BeEmpty();
    }

    // ---- Cambio de rol borra las excepciones ----

    [Fact]
    public async Task Asignar_un_rol_nuevo_borra_los_overrides_de_esa_empresa_y_publica_evento()
    {
        var s = new PermisosOverrideScenario();
        var (_, target) = await EscenarioAsync(s);
        await s.SembrarEmpresaAsync(A, "MILLET");
        var otroRol = await s.CrearRolAsync("otro", false, Leer);
        await s.AgregarOverrideAsync(target.Id, A, Crear, EfectoPermiso.Denegar);
        // Override de otra empresa: no se toca.
        await s.AgregarOverrideAsync(target.Id, PermisosOverrideScenario.EmpresaB, Crear, EfectoPermiso.Denegar);
        s.Events.Publicados.Clear();
        var handler = new AsignarRolAUsuarioHandler(
            s.Db, s.Events, s.CurrentUser, s.EmpresaContext, s.Cache, s.Clock);

        await handler.Handle(new AsignarRolAUsuarioCommand(target.Id, A, otroRol.Id), CancellationToken.None);

        (await s.Db.UsuarioPermisoOverrides.Where(o => o.EmpresaId == A).CountAsync()).Should().Be(0);
        (await s.Db.UsuarioPermisoOverrides.Where(o => o.EmpresaId == PermisosOverrideScenario.EmpresaB).CountAsync())
            .Should().Be(1);
        s.Cache.Invalidaciones.Should().Contain((target.Id, A));
        s.Events.Publicados.OfType<UsuarioPermisosOverrideActualizadosEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task Reasignar_el_mismo_rol_no_borra_nada()
    {
        var s = new PermisosOverrideScenario();
        var (_, target) = await EscenarioAsync(s);
        await s.SembrarEmpresaAsync(A, "MILLET");
        var rolId = (await s.Db.UsuarioEmpresaRoles.FirstAsync(x => x.UsuarioId == target.Id)).RolId;
        await s.AgregarOverrideAsync(target.Id, A, Crear, EfectoPermiso.Denegar);
        var handler = new AsignarRolAUsuarioHandler(
            s.Db, s.Events, s.CurrentUser, s.EmpresaContext, s.Cache, s.Clock);

        var act = () => handler.Handle(
            new AsignarRolAUsuarioCommand(target.Id, A, rolId), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        (await s.Db.UsuarioPermisoOverrides.CountAsync()).Should().Be(1);
        s.Events.Publicados.OfType<UsuarioPermisosOverrideActualizadosEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Revocar_el_rol_borra_los_overrides_de_esa_empresa()
    {
        var s = new PermisosOverrideScenario();
        var (_, target) = await EscenarioAsync(s);
        var asignacion = await s.Db.UsuarioEmpresaRoles.FirstAsync(x => x.UsuarioId == target.Id);
        await s.AgregarOverrideAsync(target.Id, A, Crear, EfectoPermiso.Denegar);
        s.Events.Publicados.Clear();
        var handler = new RevocarRolDeUsuarioHandler(
            s.Db, s.Events, s.EmpresaContext, s.Cache, s.Clock);

        await handler.Handle(new RevocarRolDeUsuarioCommand(asignacion.Id), CancellationToken.None);

        (await s.Db.UsuarioPermisoOverrides.CountAsync()).Should().Be(0);
        s.Cache.Invalidaciones.Should().Contain((target.Id, A));
        s.Events.Publicados.OfType<UsuarioPermisosOverrideActualizadosEvent>().Should().ContainSingle();
        s.Events.Publicados.OfType<UsuarioRolRevocadoEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task Revocar_sin_overrides_no_publica_el_evento_de_overrides()
    {
        var s = new PermisosOverrideScenario();
        var (_, target) = await EscenarioAsync(s);
        var asignacion = await s.Db.UsuarioEmpresaRoles.FirstAsync(x => x.UsuarioId == target.Id);
        s.Events.Publicados.Clear();
        var handler = new RevocarRolDeUsuarioHandler(
            s.Db, s.Events, s.EmpresaContext, s.Cache, s.Clock);

        await handler.Handle(new RevocarRolDeUsuarioCommand(asignacion.Id), CancellationToken.None);

        s.Events.Publicados.OfType<UsuarioPermisosOverrideActualizadosEvent>().Should().BeEmpty();
    }
}
