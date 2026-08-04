using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.UnitTests.Pedidos;

/// <summary>
/// Tests de <see cref="CanalVentaPorClaveAwResolver"/> (FAC-ING-PR2): match
/// case-insensitive con trim sobre <c>compartido.canales_venta.clave_aw</c>
/// (GRUPPE crudos con espacios y acentos), solo canales activos. InMemory
/// provider — el <c>upper()</c> real de Postgres se valida en el E2E.
/// </summary>
public class CanalVentaPorClaveAwResolverTests
{
    private static CompartidoDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase($"canales-venta-{Guid.NewGuid():N}")
            .Options;
        return new CompartidoDbContext(options, new BypassedEmpresaContext());
    }

    private static async Task SembrarAsync(CompartidoDbContext db, params CanalVenta[] canales)
    {
        db.CanalesVenta.AddRange(canales);
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("Ventas Cancun")]
    [InlineData("VENTAS CANCUN")]
    [InlineData("ventas cancun")]
    [InlineData("  Ventas Cancun  ")]
    public async Task Resuelve_CaseInsensitive_ConTrim(string clave)
    {
        using var db = NewDb();
        await SembrarAsync(db, new CanalVenta(1, "Tienda Cancún", claveAw: "Ventas Cancun"));

        var resultado = await new CanalVentaPorClaveAwResolver(db)
            .ResolverAsync(clave, CancellationToken.None);

        resultado.Should().Be((short)1);
    }

    [Fact]
    public async Task Resuelve_GruppeConAcentos()
    {
        // GRUPPE real confirmado en SER-DATA: 'CC Mérida' (con acento).
        using var db = NewDb();
        await SembrarAsync(db, new CanalVenta(4, "CC Mérida", claveAw: "CC Mérida"));

        var resultado = await new CanalVentaPorClaveAwResolver(db)
            .ResolverAsync("cc mérida", CancellationToken.None);

        resultado.Should().Be((short)4);
    }

    [Fact]
    public async Task ClaveDesconocida_DevuelveNull()
    {
        using var db = NewDb();
        await SembrarAsync(db, new CanalVenta(1, "Tienda Cancún", claveAw: "Ventas Cancun"));

        var resultado = await new CanalVentaPorClaveAwResolver(db)
            .ResolverAsync("GRUPPE Inexistente", CancellationToken.None);

        resultado.Should().BeNull();
    }

    [Fact]
    public async Task CanalInactivo_NoMachea()
    {
        // Ingestar hacia un canal desactivado debe caer a la bandeja.
        using var db = NewDb();
        await SembrarAsync(db, new CanalVenta(
            8, "Exportación", claveAw: "Ventas Internacionales",
            estatus: EstatusCatalogo.Inactivo));

        var resultado = await new CanalVentaPorClaveAwResolver(db)
            .ResolverAsync("Ventas Internacionales", CancellationToken.None);

        resultado.Should().BeNull();
    }

    [Fact]
    public async Task CanalSinClaveAw_NoMachea()
    {
        using var db = NewDb();
        await SembrarAsync(db, new CanalVenta(10, "Administración"));

        var resultado = await new CanalVentaPorClaveAwResolver(db)
            .ResolverAsync("Administración", CancellationToken.None);

        resultado.Should().BeNull();
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
