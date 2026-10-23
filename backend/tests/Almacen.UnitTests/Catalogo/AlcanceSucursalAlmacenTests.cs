using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Catalogo;

/// <summary>Alcance por sucursal de sub-almacenes (ADR-0051): helper de filtrado y verificación por id.</summary>
public class AlcanceSucursalAlmacenTests
{
    private static readonly Guid SucursalA = Guid.NewGuid();
    private static readonly Guid SucursalB = Guid.NewGuid();
    private static readonly Guid AlmacenA = Guid.NewGuid();
    private static readonly Guid AlmacenB = Guid.NewGuid();
    private static readonly Guid SubA = Guid.NewGuid();
    private static readonly Guid SubB = Guid.NewGuid();

    [Fact]
    public async Task SubAlmacenesPermitidos_devuelve_solo_los_subs_de_la_sucursal_del_usuario()
    {
        await using var db = await NuevaDbAsync();

        var permitidos = await AlcanceSucursalAlmacen.SubAlmacenesPermitidos(db, [SucursalA]).ToListAsync();

        permitidos.Should().BeEquivalentTo([SubA]);
    }

    [Fact]
    public async Task VerificarSubAlmacenAsync_lanza_Forbidden_si_el_sub_es_de_otra_sucursal()
    {
        await using var db = await NuevaDbAsync();

        var accion = () => AlcanceSucursalAlmacen.VerificarSubAlmacenAsync(db, SubB, [SucursalA], CancellationToken.None);

        var ex = await accion.Should().ThrowAsync<ForbiddenException>();
        ex.Which.Code.Should().Be("SUCURSAL_NO_ASOCIADA");
    }

    [Fact]
    public async Task VerificarSubAlmacenAsync_no_lanza_con_alcance_corporativo_o_sin_sub()
    {
        await using var db = await NuevaDbAsync();

        await AlcanceSucursalAlmacen.VerificarSubAlmacenAsync(db, SubB, null, CancellationToken.None);
        await AlcanceSucursalAlmacen.VerificarSubAlmacenAsync(db, null, [SucursalA], CancellationToken.None);
    }

    private static async Task<AlmacenDbContext> NuevaDbAsync()
    {
        var opts = new DbContextOptionsBuilder<InMemoryAlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-alcance-subs-{Guid.NewGuid():N}")
            .Options;
        var db = new InMemoryAlmacenDbContext(opts, new BypassedEmpresaContext());
        await db.Database.EnsureCreatedAsync();
        db.Almacenes.Add(new Millet.Almacen.Domain.Catalogo.Almacen(AlmacenA, "ALM-A", "Almacén A", SucursalA));
        db.Almacenes.Add(new Millet.Almacen.Domain.Catalogo.Almacen(AlmacenB, "ALM-B", "Almacén B", SucursalB));
        await db.SaveChangesAsync();
        db.SubAlmacenes.Add(new SubAlmacen(SubA, AlmacenA, "SUB-A", "Sub A", TipoSubAlmacen.Insumos));
        db.SubAlmacenes.Add(new SubAlmacen(SubB, AlmacenB, "SUB-B", "Sub B", TipoSubAlmacen.Insumos));
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
