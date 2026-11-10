using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.AppAlmacenes;

/// <summary>M-159 (InMemory): ENTRADA_ALMACEN entra al almacén general de la sucursal, sin rack.</summary>
public class EntradaAppAlmacenesTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly Guid Sucursal = Guid.NewGuid();
    private static readonly Guid Articulo = Guid.NewGuid();
    private static readonly Guid Variante = Guid.NewGuid();

    /// <summary>Sucursal con insumos y materiales directos (cada uno con su ÚNICA y un rack) y MAT-REV.</summary>
    private static (AlmacenDbContext Db, Guid UnicaDirectos, Guid SubDirectos) Db()
    {
        var db = new AlmacenDbContext(new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"entrada-app-{Guid.NewGuid():N}").Options, new FakeEmpresa(EmpresaId));
        db.Database.EnsureCreated();
        var alm = Guid.NewGuid(); var ins = Guid.NewGuid(); var dir = Guid.NewGuid(); var rev = Guid.NewGuid();
        var unicaDir = Guid.NewGuid();
        db.Almacenes.Add(new Domain.Catalogo.Almacen(alm, "ALM-G", "General", Sucursal));
        db.SubAlmacenes.Add(new SubAlmacen(ins, alm, "INS", "Insumos", TipoSubAlmacen.Insumos));
        db.SubAlmacenes.Add(new SubAlmacen(dir, alm, "DIR", "Directos", TipoSubAlmacen.MaterialesDirectos));
        db.SubAlmacenes.Add(new SubAlmacen(rev, alm, "REV", "Revisión", TipoSubAlmacen.MaterialEnRevision));
        db.Ubicaciones.Add(new Ubicacion(Guid.NewGuid(), ins, "ÚNICA", "Única", esDefault: true));
        db.Ubicaciones.Add(new Ubicacion(unicaDir, dir, "ÚNICA", "Única", esDefault: true));
        db.Ubicaciones.Add(new Ubicacion(Guid.NewGuid(), dir, "R-1", "Rack 1"));
        db.Ubicaciones.Add(new Ubicacion(Guid.NewGuid(), rev, "ÚNICA", "Única", esDefault: true));
        db.SaveChanges();
        return (db, unicaDir, dir);
    }

    private static EntradaApp Hojas(decimal hojas) => new(Articulo, "M2", Variante, hojas * 20.1422m, 3302m, 6100m);

    [Fact]
    public async Task Diez_hojas_3302x6100_entran_al_almacen_general_sin_rack_con_un_folio_ENT()
    {
        var (db, unica, _) = Db();
        var (estado, folio, error) = await EntradaAppAlmacenes.RegistrarAsync(
            db, EmpresaId, Usuario, Sucursal, "App Almacenes ENTRADA_ALMACEN EN-1", [Hojas(10)], default);
        await db.SaveChangesAsync();

        estado.Should().Be(SalidaAppAlmacenes.Recibido);
        error.Should().BeNull();
        folio.Should().StartWith("M-ENT");
        var mov = await db.Movimientos.Include(m => m.Lineas).SingleAsync();
        mov.Tipo.Should().Be(TipoMovimiento.EntradaCompra);
        mov.Motivo.Should().Be("App Almacenes ENTRADA_ALMACEN EN-1");
        mov.Lineas.Should().ContainSingle(l => l.Cantidad == 201.422m && l.UbicacionId == unica
            && l.ProductoAwVarianteId == Variante && l.AnchoMm == 3302m && l.AltoMm == 6100m);
    }

    [Fact]
    public async Task Con_saldo_previo_entra_al_promedio_vigente_para_no_moverlo()
    {
        var (db, unica, sub) = Db();
        db.SaldosInventario.Add(new SaldoInventario(unica, sub, Articulo, 50m, 210.5m, Variante));
        await db.SaveChangesAsync();

        await EntradaAppAlmacenes.RegistrarAsync(db, EmpresaId, Usuario, Sucursal, "m", [Hojas(1)], default);
        await db.SaveChangesAsync();

        (await db.LineasMovimiento.SingleAsync()).CostoUnitarioMxn.Should().Be(210.5m);
    }

    [Fact]
    public async Task Sucursal_sin_almacen_general_da_ERROR_sin_movimiento()
    {
        var (db, _, _) = Db();
        var (estado, folio, error) = await EntradaAppAlmacenes.RegistrarAsync(
            db, EmpresaId, Usuario, Guid.NewGuid(), "m", [Hojas(1)], default);

        estado.Should().Be("ERROR");
        folio.Should().BeNull();
        error.Should().Contain("almacén general");
        (await db.Movimientos.CountAsync()).Should().Be(0);
    }

    private sealed class FakeEmpresa(Guid current) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = current;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }
}
