using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Recepciones;

public class UbicacionRevisionResolverTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid AlmacenId = Guid.NewGuid();
    private static readonly Guid SubId = Guid.NewGuid();
    private static readonly Guid SubRevId = Guid.NewGuid();
    private static readonly Guid RevBinId = Guid.NewGuid();

    private static async Task<InMemoryAlmacenDbContext> NuevaDbAsync(bool conRack)
    {
        var opts = new DbContextOptionsBuilder<InMemoryAlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-rev-{Guid.NewGuid():N}").Options;
        var db = new InMemoryAlmacenDbContext(opts, new FakeEmpresa());
        await db.Database.EnsureCreatedAsync();
        db.Almacenes.Add(new Almacen.Domain.Catalogo.Almacen(AlmacenId, "ALM", "Almacén", Guid.NewGuid()));
        db.SubAlmacenes.Add(new SubAlmacen(SubId, AlmacenId, "INS", "Insumos", TipoSubAlmacen.Insumos));
        db.SubAlmacenes.Add(new SubAlmacen(SubRevId, AlmacenId, "MAT-REV", "Material en revisión", TipoSubAlmacen.MaterialEnRevision));
        db.Ubicaciones.Add(new Ubicacion(Guid.NewGuid(), SubRevId, "UNICA", "Única", esDefault: true));
        if (conRack) db.Ubicaciones.Add(new Ubicacion(RevBinId, SubRevId, "REV-01", "Revisión 1"));
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Resuelve_el_rack_REV_01_del_mismo_almacen()
    {
        await using var db = await NuevaDbAsync(conRack: true);

        var (sub, ubicacion) = await UbicacionRevisionResolver.ResolverAsync(db, SubId, default);

        sub.Should().Be(SubRevId);
        ubicacion.Should().Be(RevBinId);
    }

    [Fact]
    public async Task Solo_con_la_unica_lanza_RECEPCION_SIN_UBICACION_REVISION()
    {
        await using var db = await NuevaDbAsync(conRack: false);

        var act = () => UbicacionRevisionResolver.ResolverAsync(db, SubId, default);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("RECEPCION_SIN_UBICACION_REVISION");
    }

    private sealed class FakeEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
