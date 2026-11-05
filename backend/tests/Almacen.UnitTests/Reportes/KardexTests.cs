using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Reportes;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Cierre;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Reportes;

/// <summary>F1-ALM-15: kardex con saldo inicial, saldo y costo por renglón, cuadre con el cierre y alcance por sucursal.</summary>
public sealed class KardexTests
{
    private static readonly Guid SucCun = Guid.NewGuid(), SucCkl = Guid.NewGuid(), SubCun = Guid.NewGuid(), SubCkl = Guid.NewGuid(),
        RackCun = Guid.NewGuid(), RackCkl = Guid.NewGuid(), Articulo = Guid.NewGuid();
    private static int _folio;

    [Fact]
    public async Task Octubre_con_saldo_inicial_10_entrada_20_salida_5_y_ajuste_menos_1_termina_en_24_con_saldo_y_costo_por_renglon()
    {
        await using var db = await CrearDbAsync();
        Mov(db, TipoMovimiento.EntradaCompra, 10m, new(2027, 9, 15), costo: 100m);
        Mov(db, TipoMovimiento.EntradaCompra, 20m, new(2027, 10, 3), costo: 130m);
        Mov(db, TipoMovimiento.SalidaConsumo, 5m, new(2027, 10, 10), costo: 100m);
        Mov(db, TipoMovimiento.AjusteNegativo, 1m, new(2027, 10, 20), costo: 100m);
        await db.SaveChangesAsync();

        var r = await Handler(db).Handle(new KardexQuery(Articulo, new(2027, 10, 1), new(2027, 10, 31)), default);

        r.Filas.Select(f => (f.Tipo, f.Entrada, f.Salida, f.Saldo)).Should().Equal(
            (KardexHandler.TipoSaldoInicial, 0m, 0m, 10m),
            ("Entrada por compra", 20m, 0m, 30m),
            ("Salida por consumo", 0m, 5m, 25m),
            ("Ajuste negativo", 0m, 1m, 24m));
        r.Filas[0].ValorSaldo.Should().Be(1000m);
        r.Filas[1].ValorSaldo.Should().Be(3600m);
        r.Filas[1].CostoPromedioMxn.Should().Be(120m);
        r.Filas[3].ValorSaldo.Should().Be(3000m);
        r.Totales!["entrada"].Should().Be(20m);
        r.Totales["salida"].Should().Be(6m);
        r.Totales["saldo"].Should().Be(24m);
        r.FiltrosAplicados["saldo_inicial"].Should().Be("10");
        r.FiltrosAplicados["saldo_final"].Should().Be("24");
    }

    [Fact]
    public async Task Mes_cerrado_compara_el_saldo_final_con_la_foto_del_cierre()
    {
        await using var db = await CrearDbAsync();
        Mov(db, TipoMovimiento.EntradaCompra, 10m, new(2027, 8, 15), costo: 100m);
        Mov(db, TipoMovimiento.EntradaCompra, 5m, new(2027, 9, 10), costo: 100m);
        Mov(db, TipoMovimiento.SalidaConsumo, 2m, new(2027, 10, 1), costo: 100m); // posterior al corte: no cuenta
        var periodo = new PeriodoCerrado(Guid.NewGuid(), P1Fixture.EmpresaId, 2027, 9, Guid.NewGuid());
        db.PeriodosCerrados.Add(periodo);
        db.ValuacionesCierre.Add(new ValuacionCierre(periodo, SubCun, Articulo, null, null, 15m, 1500m));
        await db.SaveChangesAsync();

        var r = await Handler(db).Handle(new KardexQuery(Articulo, new(2027, 9, 1), new(2027, 9, 30)), default);

        r.Totales!["saldo"].Should().Be(15m);
        r.FiltrosAplicados["saldo_cierre"].Should().Be("15");
        r.FiltrosAplicados["cuadra_con_cierre"].Should().Be("sí");
    }

    [Fact]
    public async Task Sin_cierre_del_mes_no_compara_y_filtrar_por_ubicacion_tampoco()
    {
        await using var db = await CrearDbAsync();
        Mov(db, TipoMovimiento.EntradaCompra, 10m, new(2027, 9, 15), costo: 100m);
        await db.SaveChangesAsync();

        var r = await Handler(db).Handle(new KardexQuery(Articulo, new(2027, 9, 1), new(2027, 9, 30)), default);
        var u = await Handler(db).Handle(new KardexQuery(Articulo, new(2027, 9, 1), new(2027, 9, 30), UbicacionId: RackCun), default);

        r.FiltrosAplicados.Should().NotContainKey("saldo_cierre");
        u.FiltrosAplicados.Should().NotContainKey("cuadra_con_cierre");
    }

    [Fact]
    public async Task Usuario_de_Cancun_no_ve_la_ubicacion_de_Conkal_y_su_kardex_no_trae_movimientos_de_Conkal()
    {
        await using var db = await CrearDbAsync();
        Mov(db, TipoMovimiento.EntradaCompra, 10m, new(2027, 10, 3), costo: 100m, rack: RackCun);
        Mov(db, TipoMovimiento.EntradaCompra, 99m, new(2027, 10, 4), costo: 100m, rack: RackCkl);
        await db.SaveChangesAsync();
        var cancun = new KardexQuery(Articulo, new(2027, 10, 1), new(2027, 10, 31), UbicacionId: RackCkl) { SucursalesPermitidas = [SucCun] };

        await Assert.ThrowsAsync<ForbiddenException>(() => Handler(db).Handle(cancun, default));
        var propio = await Handler(db).Handle(cancun with { UbicacionId = null }, default);

        propio.Totales!["saldo"].Should().Be(10m);
    }

    [Fact]
    public async Task Filtrar_por_ubicacion_deja_solo_sus_movimientos()
    {
        await using var db = await CrearDbAsync();
        Mov(db, TipoMovimiento.EntradaCompra, 10m, new(2027, 10, 3), costo: 100m, rack: RackCun);
        Mov(db, TipoMovimiento.EntradaCompra, 99m, new(2027, 10, 4), costo: 100m, rack: RackCkl);
        await db.SaveChangesAsync();

        var r = await Handler(db).Handle(new KardexQuery(Articulo, new(2027, 10, 1), new(2027, 10, 31), UbicacionId: RackCkl), default);

        r.Totales!["saldo"].Should().Be(99m);
        r.Filas[r.Filas.Count - 1].Ubicacion.Should().Be("ALM-CKL · R-CKL");
    }

    private static KardexHandler Handler(InMemoryAlmacenDbContext db) => new(db, new ArticulosDoble());

    private static void Mov(InMemoryAlmacenDbContext db, TipoMovimiento tipo, decimal cantidad, DateOnly fecha, decimal costo, Guid? rack = null)
    {
        var m = new MovimientoInventario(Guid.CreateVersion7(), tipo, P1Fixture.EmpresaId, fecha);
        m.AgregarLinea(new(Guid.NewGuid(), m.Id, 1, Articulo, cantidad, "PZA", costo, ubicacionId: rack ?? RackCun));
        m.Registrar(FolioMovimiento.Construir(tipo, 2027, ++_folio), Guid.NewGuid());
        db.Movimientos.Add(m);
    }

    private static async Task<InMemoryAlmacenDbContext> CrearDbAsync()
    {
        var db = new InMemoryAlmacenDbContext(new DbContextOptionsBuilder<InMemoryAlmacenDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new P1Fixture.Contexto());
        var almCun = Guid.NewGuid();
        var almCkl = Guid.NewGuid();
        db.Almacenes.Add(new Domain.Catalogo.Almacen(almCun, "ALM-CUN", "Cancún", SucCun));
        db.Almacenes.Add(new Domain.Catalogo.Almacen(almCkl, "ALM-CKL", "Conkal", SucCkl));
        db.SubAlmacenes.Add(new SubAlmacen(SubCun, almCun, "SUB-CUN", "Insumos Cancún", TipoSubAlmacen.Insumos));
        db.SubAlmacenes.Add(new SubAlmacen(SubCkl, almCkl, "SUB-CKL", "Insumos Conkal", TipoSubAlmacen.Insumos));
        db.Ubicaciones.Add(new Ubicacion(RackCun, SubCun, "R-CUN", "Rack Cancún"));
        db.Ubicaciones.Add(new Ubicacion(RackCkl, SubCkl, "R-CKL", "Rack Conkal"));
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class ArticulosDoble : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<ArticuloLectura?>(null);
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(new Dictionary<Guid, ArticuloLectura>());
    }
}
