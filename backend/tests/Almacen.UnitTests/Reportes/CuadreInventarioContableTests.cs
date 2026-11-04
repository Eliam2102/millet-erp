using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Cierre;
using Millet.Almacen.Application.Reportes;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Cierre;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.UnitTests.TestSupport;

namespace Millet.Almacen.UnitTests.Reportes;

/// <summary>
/// F1-ALM-13-a: cuadre de la valuación al cierre contra la cuenta de inventario (doble del puerto de Contabilidad), foto de la
/// valuación al cierre mensual y hojas del vidrio por medida.
/// </summary>
public sealed class CuadreInventarioContableTests
{
    private static readonly Guid SucA = Guid.NewGuid(), SucB = Guid.NewGuid();
    private static readonly Guid SubA = Guid.NewGuid(), SubB = Guid.NewGuid();
    private static readonly Guid RackA = Guid.NewGuid(), RackB = Guid.NewGuid();
    private static readonly Guid Articulo = Guid.NewGuid();
    private static readonly Guid Cuenta = Guid.NewGuid();

    [Fact]
    public async Task Noviembre_cerrado_compara_la_foto_contra_la_cuenta_y_avisa_la_sucursal_sin_cuenta()
    {
        await using var db = await CrearDbAsync();
        var periodo = await CerrarAsync(db, (SubA, 10m, 9_000m), (SubB, 5m, 500m));

        var r = await Handler(db, new() { [SucA] = new(Cuenta, "1150-001", "Inventario MP", 8_000m), [SucB] = new(null, null, null, null) })
            .Handle(new CuadreInventarioContableQuery(2027, 11), default);

        var a = r.Filas.Single(f => f.Nivel == "Sucursal" && f.SucursalId == SucA);
        (a.Valuacion, a.SaldoContable, a.Diferencia, a.Observacion, a.Cuenta).Should().Be((9_000m, 8_000m, 1_000m, "No cuadra", "1150-001 · Inventario MP"));
        r.Filas.Single(f => f.Nivel == "Almacén" && f.SucursalId == SucA).Valuacion.Should().Be(9_000m);
        var b = r.Filas.Single(f => f.Nivel == "Sucursal" && f.SucursalId == SucB);
        (b.Valuacion, b.SaldoContable, b.Diferencia, b.Observacion).Should().Be((500m, (decimal?)null, (decimal?)null, CuadreInventarioContableHandler.SinCuenta));
        r.Totales!["valuacion"].Should().Be(9_500m);
        (r.Totales["saldoContable"], r.Totales["diferencia"]).Should().Be((8_000m, 1_000m));
        r.FiltrosAplicados["foto_cierre"].Should().Be("sí");
        periodo.Mes.Should().Be(11);
    }

    [Fact]
    public async Task Cuadra_cuando_el_saldo_es_igual_a_la_valuacion()
    {
        await using var db = await CrearDbAsync();
        await CerrarAsync(db, (SubA, 10m, 9_000m), (SubB, 5m, 0m));

        var r = await Handler(db, new() { [SucA] = new(Cuenta, "1150", "Inventario", 9_000m), [SucB] = new(Cuenta, "1150", "Inventario", 0m) })
            .Handle(new CuadreInventarioContableQuery(2027, 11), default);

        r.Filas.Where(f => f.Nivel == "Sucursal").Should().OnlyContain(f => f.Diferencia == 0m && f.Observacion == "Cuadra");
    }

    [Fact]
    public async Task La_foto_del_cierre_manda_solo_al_ultimo_dia_del_mes()
    {
        await using var db = await CrearDbAsync();
        await CerrarAsync(db, (SubA, 10m, 9_000m));
        var calculada = new AlfakHistorialFila(SubA, Articulo, 0m, 10m, 0m, 0m, 0m, 10m, 1_000m, 10_000m, null, null, null);

        var fin = await AlfakHistorialAlmacenHandler.AplicarFotoCierreAsync(db, new DateOnly(2027, 11, 30), [calculada], default);
        (fin[0].ValorInventarioFinal, fin[0].CostoPromedioFinal).Should().Be((9_000m, 900m));

        var medio = await AlfakHistorialAlmacenHandler.AplicarFotoCierreAsync(db, new DateOnly(2027, 11, 29), [calculada], default);
        medio[0].ValorInventarioFinal.Should().Be(10_000m);
    }

    [Fact]
    public async Task El_cierre_guarda_la_existencia_de_cada_grupo_al_fin_de_mes()
    {
        await using var db = await CrearDbAsync();
        Movimiento(db, TipoMovimiento.EntradaCompra, RackA, 10m, new(2027, 11, 5));
        Movimiento(db, TipoMovimiento.SalidaConsumo, RackA, 4m, new(2027, 11, 20));
        Movimiento(db, TipoMovimiento.EntradaCompra, RackA, 7m, new(2027, 12, 2)); // después del corte
        await db.SaveChangesAsync();
        var periodo = new PeriodoCerrado(Guid.NewGuid(), P1Fixture.EmpresaId, 2027, 11, Guid.NewGuid());
        db.PeriodosCerrados.Add(periodo);

        await EjecutarCierreMensualHandler.TomarFotoValuacionAsync(db, periodo, default);
        await db.SaveChangesAsync();

        var foto = await db.ValuacionesCierre.SingleAsync();
        (foto.SubAlmacenId, foto.ArticuloId, foto.Cantidad, foto.Anio, foto.Mes).Should().Be((SubA, Articulo, 6m, 2027, 11));
    }

    [Fact]
    public void Kardex_lista_solo_almacen_y_articulo_donde_cierre_no_es_anterior_mas_movimientos()
    {
        Guid otro = Guid.NewGuid();
        var porSub = new Dictionary<Guid, (Guid, string)> { [SubA] = (SucA, "ALM-A"), [SubB] = (SucB, "ALM-B") };
        var r = CuadreInventarioContableHandler.Conciliar(
            cierre: [(SubA, Articulo, 19_000m), (SubB, otro, 1_500m)],
            anterior: [(SubA, Articulo, 9_000m), (SubB, otro, 1_000m)],
            movimientos: [(SubA, Articulo, 10m * 1_000m), (SubB, otro, 300m)],
            porSub);

        var fila = r.Should().ContainSingle().Which;   // ALM-A cuadra: 9,000 + 10 × 1,000 = 19,000
        (fila.SucursalId, fila.ArticuloId, fila.Cierre, fila.Anterior, fila.Movimientos, fila.Diferencia).Should().Be((SucB, otro, 1_500m, 1_000m, 300m, 200m));
    }

    [Theory]
    [InlineData(178.608, 20)] // 20 hojas de 2440 × 3660 (8.9304 m² c/u)
    [InlineData(0, 0)]
    public void Hojas_del_vidrio_por_medida_salen_de_los_m2(double m2, double hojas)
    {
        var variante = Guid.NewGuid();
        var medidas = new Dictionary<Guid, VarianteVidrioLectura> { [variante] = new(variante, "2440X3660", 2440m, 3660m, 6m) };

        MedidaReporte.Hojas(variante, (decimal)m2, medidas).Should().Be((decimal)hojas);
        MedidaReporte.Hojas(null, (decimal)m2, medidas).Should().BeNull();
    }

    private static CuadreInventarioContableHandler Handler(InMemoryAlmacenDbContext db, Dictionary<Guid, CuentaInventarioSaldo> cuentas) =>
        new(db, new ContabilidadDoble(cuentas), new SucursalesDoble(), new P1Fixture.Contexto(), new ArticulosDoble());

    private static async Task<PeriodoCerrado> CerrarAsync(InMemoryAlmacenDbContext db, params (Guid Sub, decimal Cantidad, decimal Valor)[] grupos)
    {
        foreach (var (sub, cantidad, _) in grupos)
            Movimiento(db, TipoMovimiento.EntradaCompra, sub == SubA ? RackA : RackB, cantidad, new(2027, 11, 10));
        var periodo = new PeriodoCerrado(Guid.NewGuid(), P1Fixture.EmpresaId, 2027, 11, Guid.NewGuid());
        db.PeriodosCerrados.Add(periodo);
        foreach (var (sub, cantidad, valor) in grupos)
            db.ValuacionesCierre.Add(new ValuacionCierre(periodo, sub, Articulo, null, null, cantidad, valor));
        await db.SaveChangesAsync();
        return periodo;
    }

    private static void Movimiento(InMemoryAlmacenDbContext db, TipoMovimiento tipo, Guid rack, decimal cantidad, DateOnly fecha)
    {
        var m = new MovimientoInventario(Guid.CreateVersion7(), tipo, P1Fixture.EmpresaId, fecha);
        m.AgregarLinea(new(Guid.NewGuid(), m.Id, 1, Articulo, cantidad, "PZA", 900m, ubicacionId: rack));
        m.Registrar(FolioMovimiento.Construir(tipo, 2027, db.Movimientos.Local.Count + 1), Guid.NewGuid());
        db.Movimientos.Add(m);
    }

    private static async Task<InMemoryAlmacenDbContext> CrearDbAsync()
    {
        var db = new InMemoryAlmacenDbContext(new DbContextOptionsBuilder<InMemoryAlmacenDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new P1Fixture.Contexto());
        Guid almA = Guid.NewGuid(), almB = Guid.NewGuid();
        db.Almacenes.Add(new Domain.Catalogo.Almacen(almA, "ALM-A", "Almacén A", SucA));
        db.Almacenes.Add(new Domain.Catalogo.Almacen(almB, "ALM-B", "Almacén B", SucB));
        db.SubAlmacenes.Add(new SubAlmacen(SubA, almA, "SUB-A", "Sub A", TipoSubAlmacen.Insumos));
        db.SubAlmacenes.Add(new SubAlmacen(SubB, almB, "SUB-B", "Sub B", TipoSubAlmacen.Insumos));
        db.Ubicaciones.Add(new Ubicacion(RackA, SubA, "R-A", "Rack A"));
        db.Ubicaciones.Add(new Ubicacion(RackB, SubB, "R-B", "Rack B"));
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class ContabilidadDoble(Dictionary<Guid, CuentaInventarioSaldo> cuentas) : IInventarioContableReadPort
    {
        public Task<IReadOnlyDictionary<Guid, CuentaInventarioSaldo>> ObtenerAsync(DateOnly corte, IReadOnlyCollection<Guid> sucursales, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, CuentaInventarioSaldo>>(
                sucursales.Where(cuentas.ContainsKey).ToDictionary(s => s, s => cuentas[s]));
    }

    private sealed class ArticulosDoble : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<ArticuloLectura?>(null);
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(new Dictionary<Guid, ArticuloLectura>());
    }

    private sealed class SucursalesDoble : ISucursalReadPort
    {
        public Task<SucursalLectura?> ObtenerAsync(Guid sucursalId, CancellationToken cancellationToken) =>
            Task.FromResult<SucursalLectura?>(new(sucursalId, sucursalId == SucA ? "A" : "B", "Sucursal", P1Fixture.EmpresaId, true));
    }
}
