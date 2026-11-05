using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Application.Usuarios;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Identidad.UnitTests.Infrastructure;

/// <summary>
/// 86e3yxe9m: convertir una cuenta técnica en cuenta de persona. Solo un
/// super-admin, con motivo de 15 caracteres o más, queda en la bitácora y no
/// deja al sistema sin otro super-admin activo.
/// </summary>
public sealed class ConvertirEnCuentaDePersonaTests : IDisposable
{
    private const string MotivoValido = "La cuenta es de una persona del área de sistemas";
    private readonly PermisosOverrideScenario _s = new();
    private readonly CompartidoDbContext _compartido = new(
        new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new PermisosOverrideScenario.BypassedEmpresaContext());
    private readonly AuditoriaFalsa _audit = new();
    private readonly CoreDbContext _core = new(
        new DbContextOptionsBuilder<CoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new PermisosOverrideScenario.BypassedEmpresaContext());

    public void Dispose()
    {
        _s.Db.Dispose();
        _compartido.Dispose();
        _core.Dispose();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   corto      ")]
    [InlineData("14 caracteres.")]
    public void El_motivo_debe_tener_al_menos_15_caracteres(string motivo)
    {
        var r = new ConvertirEnCuentaDePersonaValidator().Validate(
            new ConvertirEnCuentaDePersonaCommand(Guid.NewGuid(), 1, motivo));
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == nameof(ConvertirEnCuentaDePersonaCommand.Motivo));
    }

    [Fact]
    public void Un_motivo_de_15_caracteres_es_valido() =>
        new ConvertirEnCuentaDePersonaValidator().Validate(
                new ConvertirEnCuentaDePersonaCommand(Guid.NewGuid(), 1, "Quince caracter"))
            .IsValid.Should().BeTrue();

    [Fact]
    public async Task Super_admin_convierte_la_cuenta_tecnica_y_queda_en_la_bitacora_con_el_motivo()
    {
        var super = await SuperAdminAsync();
        var (actor, tecnica) = (await UsuarioAsync("actor", super), await UsuarioAsync("soporte", tecnica: true));
        _s.CurrentUser.UserId = actor.Id;

        var r = await Handler().Handle(new ConvertirEnCuentaDePersonaCommand(tecnica.Id, tecnica.Version, MotivoValido), default);

        r.EsCuentaTecnica.Should().BeFalse();
        (await _s.Db.Usuarios.AsNoTracking().SingleAsync(u => u.Id == tecnica.Id)).EsCuentaTecnica.Should().BeFalse();
        _audit.Registros.Should().ContainSingle(a => a.Operacion == "usuario.convertido-a-persona"
            && a.EntidadId == tecnica.Id && a.Resumen.Contains(MotivoValido) && a.UsuarioId == actor.Id);
    }

    [Fact]
    public async Task Si_la_bitacora_falla_la_cuenta_sigue_siendo_tecnica()
    {
        // Caso del 4-nov: la bitácora rechazó el registro y la cuenta quedó convertida sin rastro.
        var super = await SuperAdminAsync();
        var actor = await UsuarioAsync("actor", super);
        var tecnica = await UsuarioAsync("soporte", tecnica: true);
        _s.CurrentUser.UserId = actor.Id;
        _audit.Falla = true;

        var act = () => Handler().Handle(new ConvertirEnCuentaDePersonaCommand(tecnica.Id, tecnica.Version, MotivoValido), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _s.Db.ChangeTracker.Clear();
        (await _s.Db.Usuarios.AsNoTracking().SingleAsync(u => u.Id == tecnica.Id)).EsCuentaTecnica.Should().BeTrue();
    }

    [Fact]
    public async Task Quien_no_es_super_admin_no_puede_convertir()
    {
        await SuperAdminAsync();
        var otroRol = await _s.CrearRolAsync("contador");
        var actor = await UsuarioAsync("contador", otroRol);
        var tecnica = await UsuarioAsync("soporte", tecnica: true);
        _s.CurrentUser.UserId = actor.Id;

        var act = () => Handler().Handle(new ConvertirEnCuentaDePersonaCommand(tecnica.Id, tecnica.Version, MotivoValido), default);

        (await act.Should().ThrowAsync<ForbiddenException>()).Which.Code.Should().Be("USUARIO_CONVERTIR_SOLO_SUPER_ADMIN");
        _audit.Registros.Should().BeEmpty();
    }

    [Fact]
    public async Task No_convierte_al_unico_super_admin_activo_sin_empleado()
    {
        var super = await SuperAdminAsync();
        var admin = await UsuarioAsync("admin-inicial", super, tecnica: true);
        await UsuarioAsync("otro-super-inactivo", super, activo: false);
        _s.CurrentUser.UserId = admin.Id;

        var act = () => Handler().Handle(new ConvertirEnCuentaDePersonaCommand(admin.Id, admin.Version, MotivoValido), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("USUARIO_ULTIMO_SUPER_ADMIN");
        (await _s.Db.Usuarios.AsNoTracking().SingleAsync(u => u.Id == admin.Id)).EsCuentaTecnica.Should().BeTrue();
    }

    [Fact]
    public async Task Con_otro_super_admin_activo_si_convierte_al_super_admin_tecnico()
    {
        var super = await SuperAdminAsync();
        var admin = await UsuarioAsync("admin-inicial", super, tecnica: true);
        var otro = await UsuarioAsync("otro-super", super);
        _s.CurrentUser.UserId = otro.Id;

        var r = await Handler().Handle(new ConvertirEnCuentaDePersonaCommand(admin.Id, admin.Version, MotivoValido), default);

        r.EsCuentaTecnica.Should().BeFalse();
    }

    [Fact]
    public async Task El_super_admin_unico_con_empleado_si_se_convierte_porque_conserva_el_acceso()
    {
        var super = await SuperAdminAsync();
        var admin = await UsuarioAsync("admin-inicial", super, tecnica: true);
        _compartido.Empleados.Add(new Empleado(Guid.CreateVersion7(), PermisosOverrideScenario.EmpresaA, "E-1", "Admin",
            usuarioId: admin.Id));
        await _compartido.SaveChangesAsync();
        _s.CurrentUser.UserId = admin.Id;

        var r = await Handler().Handle(new ConvertirEnCuentaDePersonaCommand(admin.Id, admin.Version, MotivoValido), default);

        r.EsCuentaTecnica.Should().BeFalse();
    }

    [Fact]
    public async Task Una_cuenta_de_persona_no_se_vuelve_a_convertir()
    {
        var super = await SuperAdminAsync();
        var actor = await UsuarioAsync("actor", super);
        var persona = await UsuarioAsync("persona");
        _s.CurrentUser.UserId = actor.Id;

        var act = () => Handler().Handle(new ConvertirEnCuentaDePersonaCommand(persona.Id, persona.Version, MotivoValido), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("USUARIO_NO_ES_CUENTA_TECNICA");
    }

    [Fact]
    public async Task Version_vieja_es_conflicto()
    {
        var super = await SuperAdminAsync();
        var actor = await UsuarioAsync("actor", super);
        var tecnica = await UsuarioAsync("soporte", tecnica: true);
        _s.CurrentUser.UserId = actor.Id;

        var act = () => Handler().Handle(new ConvertirEnCuentaDePersonaCommand(tecnica.Id, tecnica.Version + 5, MotivoValido), default);

        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    private ConvertirEnCuentaDePersonaHandler Handler() =>
        new(_s.Db, _compartido, _core, _s.CurrentUser, _audit, _s.Cache);

    private Task<Rol> SuperAdminAsync() =>
        _s.CrearRolAsync(DesactivarUsuarioHandler.SuperAdminCodigo, esDelSistema: true);

    private async Task<Usuario> UsuarioAsync(string alias, Rol? rol = null, bool tecnica = false, bool activo = true)
    {
        var u = new Usuario(Guid.CreateVersion7(), $"oid-{alias}", $"{alias}@test.local", alias, esCuentaTecnica: tecnica);
        if (!activo) u.Desactivar();
        _s.Db.Usuarios.Add(u);
        if (rol is not null) _s.Db.UsuarioEmpresaRoles.Add(u.AsignarRolEnEmpresa(PermisosOverrideScenario.EmpresaA, rol.Id, null));
        await _s.Db.SaveChangesAsync();
        _s.Db.ChangeTracker.Clear();
        return u;
    }

    private sealed class AuditoriaFalsa : IAuditLogWriter
    {
        public List<(string Operacion, Guid? EntidadId, string Resumen, Guid? UsuarioId)> Registros { get; } = new();
        public bool Falla { get; set; }

        public Task RegistrarAsync(string operacion, string modulo, string entidad, Guid? entidadId, Guid? aggregateRootId,
            string actorNombre, string actorTipo, string? actorEmail, string entidadEtiqueta, string resumen,
            Guid? usuarioId = null, Guid? empresaId = null, string cambios = "{}", string? metadatos = null,
            CancellationToken cancellationToken = default)
        {
            if (Falla) throw new InvalidOperationException("La bitácora rechazó el registro.");
            // Mismo límite que core.audit_log.operacion (varchar(32)).
            if (operacion.Length > 32) throw new InvalidOperationException("operacion excede varchar(32).");
            Registros.Add((operacion, entidadId, resumen, usuarioId));
            return Task.CompletedTask;
        }
    }
}
