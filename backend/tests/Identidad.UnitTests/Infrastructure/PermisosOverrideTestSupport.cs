using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Identidad.UnitTests.Infrastructure;

/// <summary>
/// Soporte compartido de las pruebas de permisos personalizados (ADR-0053):
/// DbContext InMemory, fakes de contexto y helpers de siembra. Usa los
/// permisos canónicos reales (el seed <c>HasData</c> los inserta).
/// </summary>
internal sealed class PermisosOverrideScenario
{
    public static readonly Guid EmpresaA = Guid.CreateVersion7();
    public static readonly Guid EmpresaB = Guid.CreateVersion7();

    public IdentidadDbContext Db { get; }
    public FakeUserContext CurrentUser { get; } = new();
    public FakePermissionCache Cache { get; } = new();
    public FakeEventPublisher Events { get; } = new();
    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
    public BypassedEmpresaContext EmpresaContext { get; } = new();
    public PermissionLoader Loader { get; }

    public PermisosOverrideScenario()
    {
        var opts = new DbContextOptionsBuilder<IdentidadDbContext>()
            .UseInMemoryDatabase($"identidad-overrides-{Guid.NewGuid():N}")
            .Options;
        Db = new IdentidadDbContext(opts, EmpresaContext);
        Db.Database.EnsureCreated();
        Loader = new PermissionLoader(Db, EmpresaContext);
    }

    public static Guid PermisoId(string codigo) =>
        PermisosCanonicos.Todos.First(p => p.Codigo == codigo).Id;

    public async Task<Rol> CrearRolAsync(
        string codigo, bool esDelSistema = false, params string[] permisos)
    {
        var rol = new Rol(Guid.CreateVersion7(), codigo, codigo, esDelSistema);
        Db.Roles.Add(rol);
        foreach (var p in permisos)
        {
            Db.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rol.Id, PermisoId(p)));
        }
        await Db.SaveChangesAsync();
        return rol;
    }

    public async Task<Usuario> CrearUsuarioAsync(string alias, Guid? empresaId = null, Rol? rol = null)
    {
        var usuario = new Usuario(Guid.CreateVersion7(), $"oid-{alias}", $"{alias}@test.local", alias);
        Db.Usuarios.Add(usuario);
        if (empresaId is Guid e && rol is not null)
        {
            Db.UsuarioEmpresaRoles.Add(usuario.AsignarRolEnEmpresa(e, rol.Id, null));
        }
        await Db.SaveChangesAsync();
        return usuario;
    }

    public async Task AgregarOverrideAsync(
        Guid usuarioId, Guid empresaId, string permiso, EfectoPermiso efecto)
    {
        Db.UsuarioPermisoOverrides.Add(new UsuarioPermisoOverride(
            Guid.CreateVersion7(), usuarioId, empresaId, PermisoId(permiso), efecto));
        await Db.SaveChangesAsync();
    }

    public async Task SembrarEmpresaAsync(Guid empresaId, string clave)
    {
        Db.Set<Empresa>().Add(new Empresa(
            empresaId, clave, "MIL010101AB1", "Millet S.A. de C.V.", "601",
            "Calle Ficticia 123", "123", "Colonia de Prueba",
            "Mérida", "Mérida", "Yucatán", "México"));
        await Db.SaveChangesAsync();
    }

    public sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Noop();

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }

    public sealed class FakeUserContext : ICurrentUserContext
    {
        public Guid? UserId { get; set; }
        public string? UserName => "test";
    }

    public sealed class FakePermissionCache : IPermissionCache
    {
        public List<(Guid UserId, Guid EmpresaId)> Invalidaciones { get; } = new();

        public Task<IReadOnlyCollection<string>?> GetAsync(Guid userId, Guid empresaId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<string>?>(null);

        public Task SetAsync(Guid userId, Guid empresaId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task InvalidateAsync(Guid userId, Guid empresaId, CancellationToken cancellationToken = default)
        {
            Invalidaciones.Add((userId, empresaId));
            return Task.CompletedTask;
        }

        public Task InvalidateAllForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    public sealed class FakeEventPublisher : IIntegrationEventPublisher
    {
        public List<object> Publicados { get; } = new();

        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken)
        {
            Publicados.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }
}
