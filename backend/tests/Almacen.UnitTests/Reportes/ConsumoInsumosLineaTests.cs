using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Reportes;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.UnitTests.TestSupport;

namespace Millet.Almacen.UnitTests.Reportes;

/// <summary>M-339: salida de rollos de PVB a una línea aparece con cantidad y costo; fuera de rango y canceladas no cuentan.</summary>
public sealed class ConsumoInsumosLineaTests
{
    private static readonly Guid Suc = Guid.NewGuid(), Sub = Guid.NewGuid(), Rack = Guid.NewGuid(), Pvb = Guid.NewGuid(), Linea = Guid.NewGuid();
    private static int _folio;

    [Fact]
    public async Task Salida_de_2_rollos_PVB_a_una_linea_es_una_fila_con_cantidad_2_y_costo_3000_y_la_de_fuera_de_rango_se_excluye()
    {
        await using var db = await CrearDbAsync();
        Salida(db, 2m, new(2027, 10, 10), 1500m, Linea);
        Salida(db, 5m, new(2027, 11, 2), 1500m, Linea);
        await db.SaveChangesAsync();

        var r = await Handler(db).Handle(new ConsumoInsumosLineaQuery(new(2027, 10, 1), new(2027, 10, 31)), default);

        var f = r.Filas.Should().ContainSingle().Subject;
        f.Linea.Should().Be("LIN-1 · Línea laminado");
        (f.ArticuloClave, f.Cantidad, f.CostoUnitarioPromedioMxn, f.CostoTotalMxn).Should().Be(("PVB", 2m, 1500m, 3000m));
        r.Totales!["costoTotalMxn"].Should().Be(3000m);
    }

    [Fact]
    public async Task Sin_centro_de_costo_cae_en_Sin_linea_y_la_devolucion_interna_resta()
    {
        await using var db = await CrearDbAsync();
        var s = Salida(db, 4m, new(2027, 10, 10), 100m, null);
        var d = new MovimientoInventario(Guid.CreateVersion7(), TipoMovimiento.DevolucionSalida, P1Fixture.EmpresaId, new(2027, 10, 12));
        d.AgregarLinea(new(Guid.NewGuid(), d.Id, 1, Pvb, 1m, "PZA", 100m, ubicacionId: Rack, lineaSalidaOrigenId: s.Lineas.Single().Id));
        d.Registrar(FolioMovimiento.Construir(TipoMovimiento.DevolucionSalida, 2027, ++_folio), Guid.NewGuid());
        db.Movimientos.Add(d);
        await db.SaveChangesAsync();

        var r = await Handler(db).Handle(new ConsumoInsumosLineaQuery(new(2027, 10, 1), new(2027, 10, 31)), default);

        var f = r.Filas.Should().ContainSingle().Subject;
        (f.Linea, f.Cantidad, f.CostoTotalMxn).Should().Be((ConsumoInsumosLineaHandler.SinLinea, 3m, 300m));
    }

    [Fact]
    public async Task Filtrar_por_centro_de_costo_deja_solo_esa_linea()
    {
        await using var db = await CrearDbAsync();
        Salida(db, 2m, new(2027, 10, 10), 10m, Linea);
        Salida(db, 9m, new(2027, 10, 10), 10m, null);
        await db.SaveChangesAsync();

        var r = await Handler(db).Handle(new ConsumoInsumosLineaQuery(new(2027, 10, 1), new(2027, 10, 31), CentroCostoId: Linea), default);

        r.Filas.Should().ContainSingle().Which.Cantidad.Should().Be(2m);
    }

    private static ConsumoInsumosLineaHandler Handler(InMemoryAlmacenDbContext db) => new(db, new Articulos(), new Centros());

    private static MovimientoInventario Salida(InMemoryAlmacenDbContext db, decimal cantidad, DateOnly fecha, decimal costo, Guid? centro)
    {
        var m = new MovimientoInventario(Guid.CreateVersion7(), TipoMovimiento.SalidaConsumo, P1Fixture.EmpresaId, fecha);
        m.AgregarLinea(new(Guid.NewGuid(), m.Id, 1, Pvb, cantidad, "PZA", costo, centroCostoId: centro, ubicacionId: Rack));
        m.Registrar(FolioMovimiento.Construir(TipoMovimiento.SalidaConsumo, 2027, ++_folio), Guid.NewGuid());
        db.Movimientos.Add(m);
        return m;
    }

    private static async Task<InMemoryAlmacenDbContext> CrearDbAsync()
    {
        var db = new InMemoryAlmacenDbContext(new DbContextOptionsBuilder<InMemoryAlmacenDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new P1Fixture.Contexto());
        var alm = Guid.NewGuid();
        db.Almacenes.Add(new Domain.Catalogo.Almacen(alm, "ALM", "Conkal", Suc));
        db.SubAlmacenes.Add(new SubAlmacen(Sub, alm, "SUB", "Materiales directos", TipoSubAlmacen.Insumos));
        db.Ubicaciones.Add(new Ubicacion(Rack, Sub, "R1", "Rack 1"));
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class Articulos : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<ArticuloLectura?>(null);
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(ids.ToDictionary(i => i, i => new ArticuloLectura(i, "PVB", "Rollo PVB", "PZA", null, true)));
    }

    private sealed class Centros : ICentroCostoReadPort
    {
        public Task<IReadOnlyDictionary<Guid, Dim3Lectura>> ObtenerAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, Dim3Lectura>>(ids.ToDictionary(i => i, i => new Dim3Lectura(i, "LIN-1", "Línea laminado", true)));
    }
}
