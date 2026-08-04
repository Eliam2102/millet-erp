using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;

namespace Millet.SharedKernel.UnitTests.Application;

public class InMemoryPermissionCacheTests
{
    private static readonly Guid User1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid User2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid EmpresaA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid EmpresaB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly string[] FacturadorPerms = ["fiscal.cfdi.timbrar", "fiscal.cfdi.cancelar.solicitar"];
    private static readonly string[] CobradorPerms = ["cobranza.pago.aplicar"];

    private static (TestClock Clock, InMemoryPermissionCache Cache) NewCache()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 5, 3, 12, 0, 0, TimeSpan.Zero));
        var cache = new InMemoryPermissionCache(clock);
        return (clock, cache);
    }

    [Fact]
    public async Task Should_ReturnNull_When_NoEntryForUser()
    {
        var (_, cache) = NewCache();

        var result = await cache.GetAsync(User1, EmpresaA);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Should_ReturnPermissions_When_FreshEntryExists()
    {
        var (_, cache) = NewCache();
        await cache.SetAsync(User1, EmpresaA, FacturadorPerms);

        var result = await cache.GetAsync(User1, EmpresaA);

        result.Should().NotBeNull().And.BeEquivalentTo(FacturadorPerms);
    }

    [Fact]
    public async Task Should_IsolateEntries_PerUserAndEmpresa()
    {
        var (_, cache) = NewCache();
        await cache.SetAsync(User1, EmpresaA, FacturadorPerms);
        await cache.SetAsync(User2, EmpresaA, CobradorPerms);

        (await cache.GetAsync(User1, EmpresaA)).Should().BeEquivalentTo(FacturadorPerms);
        (await cache.GetAsync(User2, EmpresaA)).Should().BeEquivalentTo(CobradorPerms);
        (await cache.GetAsync(User1, EmpresaB)).Should().BeNull();
    }

    [Fact]
    public async Task Should_ExpireEntry_AfterTtlElapsed()
    {
        var (clock, cache) = NewCache();
        await cache.SetAsync(User1, EmpresaA, FacturadorPerms);

        clock.Advance(TimeSpan.FromMinutes(4));
        (await cache.GetAsync(User1, EmpresaA)).Should().NotBeNull();

        clock.Advance(TimeSpan.FromMinutes(2)); // total 6 min, TTL es 5
        (await cache.GetAsync(User1, EmpresaA)).Should().BeNull();
    }

    [Fact]
    public async Task Should_RemoveEntry_When_InvalidateAsync()
    {
        var (_, cache) = NewCache();
        await cache.SetAsync(User1, EmpresaA, FacturadorPerms);

        await cache.InvalidateAsync(User1, EmpresaA);

        (await cache.GetAsync(User1, EmpresaA)).Should().BeNull();
    }

    [Fact]
    public async Task Should_RemoveAllEntriesForUser_When_InvalidateAllForUserAsync()
    {
        var (_, cache) = NewCache();
        await cache.SetAsync(User1, EmpresaA, FacturadorPerms);
        await cache.SetAsync(User1, EmpresaB, CobradorPerms);
        await cache.SetAsync(User2, EmpresaA, FacturadorPerms);

        await cache.InvalidateAllForUserAsync(User1);

        (await cache.GetAsync(User1, EmpresaA)).Should().BeNull();
        (await cache.GetAsync(User1, EmpresaB)).Should().BeNull();
        // User2 no se ve afectado
        (await cache.GetAsync(User2, EmpresaA)).Should().NotBeNull();
    }

    [Fact]
    public async Task Should_OverwriteEntry_When_SetAgainOnSameKey()
    {
        var (_, cache) = NewCache();
        await cache.SetAsync(User1, EmpresaA, FacturadorPerms);
        await cache.SetAsync(User1, EmpresaA, CobradorPerms);

        var result = await cache.GetAsync(User1, EmpresaA);

        result.Should().BeEquivalentTo(CobradorPerms);
    }
}
