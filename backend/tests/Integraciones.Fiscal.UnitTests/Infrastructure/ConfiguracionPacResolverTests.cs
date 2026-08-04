using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Infrastructure.Cifrado;
using Millet.Integraciones.Fiscal.Infrastructure.Cliente;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.UnitTests.Application;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Fiscal.UnitTests.Infrastructure;

public sealed class ConfiguracionPacResolverTests
{
    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Ahora = new(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Helper: wirea un mini DI donde cada scope crea su propio
    /// IntegracionesFiscalDbContext apuntando a la misma BD InMemory
    /// (nombre fijo). El resolver crea scope → su DbContext es desechable
    /// sin afectar al DbContext del test (instancia distinta del mismo
    /// store).
    /// </summary>
    private static (ConfiguracionPacResolver Resolver, IServiceScope TestScope, FiscalSecretCipher Cipher, ServiceProvider Sp, string DbName)
        Build()
    {
        var dbName = $"resolver-tests-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddMemoryCache();

        var cipher = InMemoryFiscalDb.Cipher();
        services.AddSingleton(cipher);
        services.AddScoped<ICurrentEmpresaContext, InMemoryFiscalDb.BypassedEmpresaContext>();

        services.AddDbContext<IntegracionesFiscalDbContext>(opts =>
            opts.UseInMemoryDatabase(dbName));

        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var cache = sp.GetRequiredService<IMemoryCache>();
        var testScope = sp.CreateScope();

        var resolver = new ConfiguracionPacResolver(scopeFactory, cache);
        return (resolver, testScope, cipher, sp, dbName);
    }

    private static IntegracionesFiscalDbContext DbFor(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IntegracionesFiscalDbContext>();

    private static ConfiguracionPac NewConfig(FiscalSecretCipher cipher, bool activo = true) =>
        new(
            id: Guid.NewGuid(),
            empresaId: EmpresaId,
            proveedor: ProveedorPac.FiscalApi,
            baseUrl: "https://api.fiscalapi.com",
            apiKeyCifrado: cipher.Encrypt("secret-123"),
            apiKeyHash: FiscalSecretCipher.HashForChangeDetection("secret-123"),
            ahora: Ahora);

    [Fact]
    public async Task Resolver_devuelve_null_si_no_existe_configuracion()
    {
        var (resolver, _, _, _, _) = Build();

        var result = await resolver.ResolverAsync(EmpresaId, ProveedorPac.FiscalApi, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Resolver_descifra_apikey_y_devuelve_snapshot()
    {
        var (resolver, scope, cipher, _, _) = Build();
        var db = DbFor(scope);
        var config = NewConfig(cipher);
        db.ConfiguracionesPac.Add(config);
        await db.SaveChangesAsync();

        var result = await resolver.ResolverAsync(EmpresaId, ProveedorPac.FiscalApi, CancellationToken.None);

        result.Should().NotBeNull();
        result!.ApiKey.Should().Be("secret-123");
        result.BaseUrl.Should().Be("https://api.fiscalapi.com");
        result.Activo.Should().BeTrue();
    }

    [Fact]
    public async Task Resolver_omite_filas_inactivas()
    {
        var (resolver, scope, cipher, _, _) = Build();
        var db = DbFor(scope);
        var config = NewConfig(cipher);
        config.Desactivar();
        db.ConfiguracionesPac.Add(config);
        await db.SaveChangesAsync();

        var result = await resolver.ResolverAsync(EmpresaId, ProveedorPac.FiscalApi, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Resolver_cachea_el_resultado_de_la_segunda_llamada()
    {
        var (resolver, scope, cipher, _, _) = Build();
        var db = DbFor(scope);
        db.ConfiguracionesPac.Add(NewConfig(cipher));
        await db.SaveChangesAsync();

        var first = await resolver.ResolverAsync(EmpresaId, ProveedorPac.FiscalApi, CancellationToken.None);
        first.Should().NotBeNull();

        // Borrar la fila — si el cache funciona, la segunda llamada
        // sigue devolviendo el snapshot anterior.
        db.ConfiguracionesPac.Remove(db.ConfiguracionesPac.Single());
        await db.SaveChangesAsync();

        var second = await resolver.ResolverAsync(EmpresaId, ProveedorPac.FiscalApi, CancellationToken.None);
        second.Should().NotBeNull();
        second!.ApiKey.Should().Be("secret-123");
    }

    [Fact]
    public async Task Invalidar_fuerza_refresh_inmediato()
    {
        var (resolver, scope, cipher, _, _) = Build();
        var db = DbFor(scope);
        db.ConfiguracionesPac.Add(NewConfig(cipher));
        await db.SaveChangesAsync();

        await resolver.ResolverAsync(EmpresaId, ProveedorPac.FiscalApi, CancellationToken.None);

        // Modificar la fila + invalidar — la próxima llamada ve el cambio.
        var config = db.ConfiguracionesPac.Single();
        config.RotarApiKey(
            cipher.Encrypt("nueva-key"),
            FiscalSecretCipher.HashForChangeDetection("nueva-key"),
            Ahora.AddMinutes(1));
        await db.SaveChangesAsync();

        resolver.Invalidar(EmpresaId, ProveedorPac.FiscalApi);

        var refreshed = await resolver.ResolverAsync(EmpresaId, ProveedorPac.FiscalApi, CancellationToken.None);
        refreshed!.ApiKey.Should().Be("nueva-key");
    }
}
