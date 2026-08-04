using Microsoft.Extensions.Caching.Memory;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Infrastructure.Adapters;

namespace Millet.Identidad.UnitTests.Infrastructure;

/// <summary>
/// Tests del decorator <see cref="CachedServicePrincipalResolver"/>. Cubren
/// hit / miss / TTL expiry / keys distintas, y verifican que NotFound y
/// Disabled también se cachean (decisión documentada — bloquea
/// AppId-spraying pero retrasa el alta de SPs nuevos).
/// </summary>
public sealed class CachedServicePrincipalResolverTests : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose() => _cache.Dispose();

    private static readonly string[] DefaultPermisos = { "integraciones.aw.cotizaciones.crear" };

    private static ResolvedServicePrincipal NewSp(Guid appId) => new(
        Id: Guid.CreateVersion7(),
        Nombre: "Test SP",
        EntraAppId: appId,
        EmpresaId: Guid.NewGuid(),
        Permisos: DefaultPermisos);

    [Fact]
    public async Task ResolveAsync_Should_Hit_Inner_On_First_Call()
    {
        var appId = Guid.NewGuid();
        var inner = new CountingResolver(_ => ServicePrincipalResolutionResult.Found(NewSp(appId)));
        var sut = new CachedServicePrincipalResolver(inner, _cache);

        var result = await sut.ResolveAsync(appId, Guid.NewGuid());

        result.Principal.Should().NotBeNull();
        inner.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ResolveAsync_Should_Hit_Cache_On_Second_Call_With_Same_AppId()
    {
        var appId = Guid.NewGuid();
        var inner = new CountingResolver(_ => ServicePrincipalResolutionResult.Found(NewSp(appId)));
        var sut = new CachedServicePrincipalResolver(inner, _cache);

        await sut.ResolveAsync(appId, Guid.NewGuid());
        await sut.ResolveAsync(appId, Guid.NewGuid()); // mismo AppId, distinto ObjectId

        inner.CallCount.Should().Be(1, "el segundo call debe servir desde cache");
    }

    [Fact]
    public async Task ResolveAsync_Should_Hit_Inner_For_Distinct_AppIds()
    {
        var inner = new CountingResolver(appId => ServicePrincipalResolutionResult.Found(NewSp(appId)));
        var sut = new CachedServicePrincipalResolver(inner, _cache);

        await sut.ResolveAsync(Guid.NewGuid(), Guid.NewGuid());
        await sut.ResolveAsync(Guid.NewGuid(), Guid.NewGuid());

        inner.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task ResolveAsync_Should_Cache_NotFound_Result()
    {
        var appId = Guid.NewGuid();
        var inner = new CountingResolver(_ => ServicePrincipalResolutionResult.NotFound());
        var sut = new CachedServicePrincipalResolver(inner, _cache);

        var first = await sut.ResolveAsync(appId, Guid.NewGuid());
        var second = await sut.ResolveAsync(appId, Guid.NewGuid());

        first.Failure.Should().Be(ServicePrincipalResolutionFailure.UnknownAppId);
        second.Failure.Should().Be(ServicePrincipalResolutionFailure.UnknownAppId);
        inner.CallCount.Should().Be(1, "NotFound también se cachea (bloquea AppId-spraying)");
    }

    [Fact]
    public async Task ResolveAsync_Should_Cache_Disabled_Result()
    {
        var appId = Guid.NewGuid();
        var inner = new CountingResolver(_ => ServicePrincipalResolutionResult.Disabled());
        var sut = new CachedServicePrincipalResolver(inner, _cache);

        await sut.ResolveAsync(appId, Guid.NewGuid());
        await sut.ResolveAsync(appId, Guid.NewGuid());

        inner.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ResolveAsync_Should_Hit_Inner_Again_After_Ttl_Expiry()
    {
        var appId = Guid.NewGuid();
        var inner = new CountingResolver(_ => ServicePrincipalResolutionResult.Found(NewSp(appId)));
        // TTL 1ms para forzar expiry inmediato.
        var sut = new CachedServicePrincipalResolver(inner, _cache, TimeSpan.FromMilliseconds(1));

        await sut.ResolveAsync(appId, Guid.NewGuid());
        await Task.Delay(50); // pasa el TTL
        await sut.ResolveAsync(appId, Guid.NewGuid());

        inner.CallCount.Should().Be(2, "tras TTL expirado debe re-hittear el inner");
    }

    private sealed class CountingResolver : IServicePrincipalResolver
    {
        private readonly Func<Guid, ServicePrincipalResolutionResult> _factory;
        public int CallCount { get; private set; }

        public CountingResolver(Func<Guid, ServicePrincipalResolutionResult> factory)
        {
            _factory = factory;
        }

        public Task<ServicePrincipalResolutionResult> ResolveAsync(
            Guid entraAppId,
            Guid entraObjectId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_factory(entraAppId));
        }
    }
}
