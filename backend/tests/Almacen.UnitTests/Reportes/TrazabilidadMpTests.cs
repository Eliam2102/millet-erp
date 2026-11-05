using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Reportes;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.UnitTests.TestSupport;

namespace Millet.Almacen.UnitTests.Reportes;

/// <summary>F1-MP-09: compra, consumos, merma y saldo de un lote; el saldo corrido suma y el lote sin consumos queda completo.</summary>
public sealed class TrazabilidadMpTests
{
    private static readonly Guid Suc = Guid.NewGuid(), Sub = Guid.NewGuid(), Rack = Guid.NewGuid(), Articulo = Guid.NewGuid(), Oc = Guid.NewGuid();
    private static int _folio;

    [Fact]
    public async Task Lote_de_50_hojas_muestra_OC_consumos_merma_y_el_saldo_suma()
    {
        await using var db = await CrearDbAsync();
        Mov(db, TipoMovimiento.EntradaCompra, 50m, new(2027, 3, 1), "L-100", oc: Oc);
        Mov(db, TipoMovimiento.SalidaConsumo, 10m, new(2027, 3, 5), "L-100", motivo: "Pedido A+W 123");
        Mov(db, TipoMovimiento.BajaPorDano, 2m, new(2027, 3, 6), "L-100", motivo: "Rotura en mesa");
        Mov(db, TipoMovimiento.EntradaCompra, 30m, new(2027, 3, 2), "L-200"); // otro lote: no aparece
        await db.SaveChangesAsync();

        var r = await new TrazabilidadMpHandler(db, new ArticulosDoble(), new OcDoble())
            .Handle(new TrazabilidadMpQuery(Articulo, new(2027, 3, 1), new(2027, 3, 31), "L-100"), default);

        r.Filas.Select(f => (f.Concepto, f.Referencia, f.Entrada, f.Salida, f.Saldo)).Should().Equal(
            (TrazabilidadMpHandler.ConceptoCompra, "OC OC-77", 50m, 0m, 50m),
            (TrazabilidadMpHandler.ConceptoConsumo, "Pedido A+W 123", 0m, 10m, 40m),
            (TrazabilidadMpHandler.ConceptoMerma, "Rotura en mesa", 0m, 2m, 38m));
        r.Totales!["saldo"].Should().Be(38m);
        r.Totales["valorSaldo"].Should().Be(38m * 900m);
    }

    [Fact]
    public async Task Lote_sin_consumos_muestra_la_recepcion_y_el_saldo_completo()
    {
        await using var db = await CrearDbAsync();
        Mov(db, TipoMovimiento.EntradaCompra, 50m, new(2027, 3, 1), "L-100", oc: Oc);
        await db.SaveChangesAsync();

        var r = await new TrazabilidadMpHandler(db, new ArticulosDoble()).Handle(new TrazabilidadMpQuery(Articulo, new(2027, 3, 1), new(2027, 3, 31), "L-100"), default);

        r.Filas.Should().ContainSingle().Which.Saldo.Should().Be(50m);
        r.Totales!["salida"].Should().Be(0m);
    }

    [Fact]
    public async Task Lo_anterior_al_periodo_es_saldo_inicial_y_lo_posterior_no_cuenta()
    {
        await using var db = await CrearDbAsync();
        Mov(db, TipoMovimiento.EntradaCompra, 50m, new(2027, 2, 1), "L-100");
        Mov(db, TipoMovimiento.SalidaConsumo, 5m, new(2027, 3, 10), "L-100");
        Mov(db, TipoMovimiento.SalidaConsumo, 9m, new(2027, 4, 10), "L-100");
        await db.SaveChangesAsync();

        var r = await new TrazabilidadMpHandler(db, new ArticulosDoble()).Handle(new TrazabilidadMpQuery(Articulo, new(2027, 3, 1), new(2027, 3, 31)), default);

        r.Filas.Select(f => (f.Concepto, f.Saldo)).Should().Equal((TrazabilidadMpHandler.ConceptoSaldoInicial, 50m), (TrazabilidadMpHandler.ConceptoConsumo, 45m));
    }

    private static void Mov(InMemoryAlmacenDbContext db, TipoMovimiento tipo, decimal cantidad, DateOnly fecha, string lote, Guid? oc = null, string? motivo = null)
    {
        var m = new MovimientoInventario(Guid.CreateVersion7(), tipo, P1Fixture.EmpresaId, fecha);
        m.AgregarLinea(new(Guid.NewGuid(), m.Id, 1, Articulo, cantidad, "PZA", 900m, ubicacionId: Rack, lote: lote));
        m.Registrar(FolioMovimiento.Construir(tipo, 2027, ++_folio), Guid.NewGuid());
        if (oc != null) typeof(MovimientoInventario).GetProperty(nameof(MovimientoInventario.OcId))!.SetValue(m, oc);
        if (motivo != null) typeof(MovimientoInventario).GetProperty(nameof(MovimientoInventario.Motivo))!.SetValue(m, motivo);
        db.Movimientos.Add(m);
    }

    private static async Task<InMemoryAlmacenDbContext> CrearDbAsync()
    {
        var db = new InMemoryAlmacenDbContext(new DbContextOptionsBuilder<InMemoryAlmacenDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new P1Fixture.Contexto());
        var alm = Guid.NewGuid();
        db.Almacenes.Add(new Domain.Catalogo.Almacen(alm, "ALM-A", "Almacén A", Suc));
        db.SubAlmacenes.Add(new SubAlmacen(Sub, alm, "SUB-A", "Sub A", TipoSubAlmacen.Insumos));
        db.Ubicaciones.Add(new Ubicacion(Rack, Sub, "R-A", "Rack A"));
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class ArticulosDoble : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<ArticuloLectura?>(null);
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(new Dictionary<Guid, ArticuloLectura>());
    }

    private sealed class OcDoble : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct) => Task.FromResult<OcLectura?>(null);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(ocIds.ToDictionary(i => i, _ => "OC-77"));
    }
}
