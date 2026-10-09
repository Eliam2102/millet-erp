using Microsoft.Extensions.Caching.Memory;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Application.Roles;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure.Adapters;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Identidad.UnitTests.Infrastructure;

/// <summary>
/// U1.0: un cambio en la matriz de permisos de un rol surte efecto en la
/// siguiente petición de todos sus usuarios (CA U1.0-a), y el cache de
/// service principals se expira con cualquier invalidación de permisos.
/// </summary>
public sealed class CambiosDeRolEfectoInmediatoTests
{
    private const string Leer = PermisosCanonicos.IdentidadUsuariosLeer;
    private const string Crear = PermisosCanonicos.IdentidadUsuariosCrear;
    private static readonly Guid A = PermisosOverrideScenario.EmpresaA;
    private static readonly Guid B = PermisosOverrideScenario.EmpresaB;

    private static AsignarPermisosARolHandler Handler(PermisosOverrideScenario s, SharedKernel.Application.IPermissionCache? cache = null) =>
        new(s.Db, s.Events, s.Clock, cache ?? s.Cache);

    [Fact]
    public async Task Quitar_permiso_invalida_a_todos_los_usuarios_del_rol_en_cada_empresa()
    {
        var s = new PermisosOverrideScenario();
        var auditor = await s.CrearRolAsync("auditor", false, Leer, Crear);
        var otroRol = await s.CrearRolAsync("otro", false, Leer);
        var usuarios = new List<Usuario>();
        for (var i = 0; i < 10; i++)
            usuarios.Add(await s.CrearUsuarioAsync($"aud{i}", A, auditor));
        var enB = await s.CrearUsuarioAsync("aud-b", B, auditor);
        var ajeno = await s.CrearUsuarioAsync("ajeno", A, otroRol);

        await Handler(s).Handle(
            new AsignarPermisosARolCommand(auditor.Id, [PermisosOverrideScenario.PermisoId(Leer)]),
            CancellationToken.None);

        s.Cache.Invalidaciones.Should().HaveCount(11);
        s.Cache.Invalidaciones.Should().Contain(usuarios.Select(u => (u.Id, A)));
        s.Cache.Invalidaciones.Should().Contain((enB.Id, B));
        s.Cache.Invalidaciones.Should().NotContain(i => i.UserId == ajeno.Id);
    }

    [Fact]
    public async Task Matriz_sin_cambios_no_invalida()
    {
        var s = new PermisosOverrideScenario();
        var rol = await s.CrearRolAsync("auditor", false, Leer);
        await s.CrearUsuarioAsync("aud", A, rol);

        await Handler(s).Handle(
            new AsignarPermisosARolCommand(rol.Id, [PermisosOverrideScenario.PermisoId(Leer)]),
            CancellationToken.None);

        s.Cache.Invalidaciones.Should().BeEmpty();
    }

    [Fact]
    public async Task Siguiente_lectura_tras_quitar_permiso_ya_no_lo_incluye()
    {
        // Mismo camino que CurrentUserPermissions/PermissionAuthorizationHandler:
        // cache primero y, en miss, PermissionLoader.
        var s = new PermisosOverrideScenario();
        var cache = new InMemoryPermissionCache(s.Clock);
        var rol = await s.CrearRolAsync("auditor", false, Leer, Crear);
        var usuario = await s.CrearUsuarioAsync("aud", A, rol);
        await cache.SetAsync(usuario.Id, A, await s.Loader.LoadForUserInEmpresaAsync(usuario.Id, A, CancellationToken.None));

        await Handler(s, cache).Handle(
            new AsignarPermisosARolCommand(rol.Id, [PermisosOverrideScenario.PermisoId(Leer)]),
            CancellationToken.None);

        (await cache.GetAsync(usuario.Id, A)).Should().BeNull("el cache se invalidó y debe recargarse");
        var recargados = await s.Loader.LoadForUserInEmpresaAsync(usuario.Id, A, CancellationToken.None);
        recargados.Should().Contain(Leer).And.NotContain(Crear);
    }

    [Fact]
    public async Task Invalidar_permisos_expira_el_cache_de_service_principals()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var signal = new ServicePrincipalCacheSignal();
        var permissionCache = new ServicePrincipalAwarePermissionCache(
            new InMemoryPermissionCache(new PermisosOverrideScenario().Clock), signal);
        var appId = Guid.NewGuid();
        var llamadas = 0;
        var inner = new ResolverContador(() =>
        {
            llamadas++;
            return ServicePrincipalResolutionResult.Found(new ResolvedServicePrincipal(
                Guid.CreateVersion7(), "SP DEMO", appId, A, [Leer]));
        });
        var sut = new CachedServicePrincipalResolver(inner, memory, signal);

        await sut.ResolveAsync(appId, Guid.NewGuid());
        await sut.ResolveAsync(appId, Guid.NewGuid());
        llamadas.Should().Be(1);

        await permissionCache.InvalidateAsync(Guid.NewGuid(), A);
        await sut.ResolveAsync(appId, Guid.NewGuid());

        llamadas.Should().Be(2, "la invalidación debe forzar recargar permisos del SP desde BD");
    }

    private sealed class ResolverContador(Func<ServicePrincipalResolutionResult> resolver) : IServicePrincipalResolver
    {
        public Task<ServicePrincipalResolutionResult> ResolveAsync(
            Guid entraAppId, Guid entraObjectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(resolver());
    }
}
