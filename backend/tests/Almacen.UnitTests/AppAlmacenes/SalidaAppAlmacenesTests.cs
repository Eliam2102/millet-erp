using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.AppAlmacenes;

/// <summary>F1-MP-07 pasos 1–3 (InMemory): salida de la app baja existencia o queda en revisión.</summary>
public class SalidaAppAlmacenesTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly Guid Sucursal = Guid.NewGuid();
    private static readonly Guid Articulo = Guid.NewGuid();
    private static readonly Guid Variante = Guid.NewGuid();

    private static (AlmacenDbContext Db, Guid SubA, Guid UbiA, Guid SubB, Guid UbiB) Db()
    {
        var db = new AlmacenDbContext(new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"salida-app-{Guid.NewGuid():N}").Options, new FakeEmpresa(EmpresaId));
        db.Database.EnsureCreated();
        var alm = Guid.NewGuid(); var subA = Guid.NewGuid(); var subB = Guid.NewGuid(); var subRev = Guid.NewGuid();
        var ubiA = Guid.NewGuid(); var ubiB = Guid.NewGuid(); var ubiRev = Guid.NewGuid();
        db.Almacenes.Add(new Domain.Catalogo.Almacen(alm, "ALM-G", "General", Sucursal));
        db.SubAlmacenes.Add(new SubAlmacen(subA, alm, "INS", "Insumos", TipoSubAlmacen.Insumos));
        db.SubAlmacenes.Add(new SubAlmacen(subB, alm, "DIR", "Directos", TipoSubAlmacen.MaterialesDirectos));
        db.SubAlmacenes.Add(new SubAlmacen(subRev, alm, "REV", "Revisión", TipoSubAlmacen.MaterialEnRevision));
        db.Ubicaciones.Add(new Ubicacion(ubiA, subA, "ÚNICA", "Única", esDefault: true));
        db.Ubicaciones.Add(new Ubicacion(ubiB, subB, "ÚNICA", "Única", esDefault: true));
        db.Ubicaciones.Add(new Ubicacion(ubiRev, subRev, "ÚNICA", "Única", esDefault: true));
        db.SaldosInventario.Add(new SaldoInventario(ubiA, subA, Articulo, 10, 100m, Variante));
        db.SaldosInventario.Add(new SaldoInventario(ubiRev, subRev, Articulo, 50, 100m, Variante)); // MAT-REV no cuenta
        db.SaveChanges();
        return (db, subA, ubiA, subB, ubiB);
    }

    private static SalidaApp Salida(decimal cantidad, decimal? costo = null, TipoMovimiento tipo = TipoMovimiento.SalidaConsumo) =>
        new(tipo, Sucursal, Articulo, "PZA", Variante, cantidad, costo, "App Almacenes AJUSTE_SALIDA", 2440m, 3660m);

    [Fact]
    public async Task Tres_hojas_generan_una_salida_de_3_a_costo_promedio()
    {
        var (db, _, ubiA, _, _) = Db();
        var (estado, folio, error) = await SalidaAppAlmacenes.RegistrarAsync(db, new Eventos(), EmpresaId, Usuario, Salida(3), default);
        await db.SaveChangesAsync();

        estado.Should().Be(SalidaAppAlmacenes.Recibido);
        error.Should().BeNull();
        folio.Should().StartWith("M-SAL");
        var mov = await db.Movimientos.Include(m => m.Lineas).SingleAsync();
        mov.Tipo.Should().Be(TipoMovimiento.SalidaConsumo);
        mov.Lineas.Should().ContainSingle(l => l.Cantidad == 3 && l.CostoUnitarioMxn == 100m && l.UbicacionId == ubiA);
    }

    [Fact]
    public async Task Usa_el_costo_del_mensaje_cuando_viene()
    {
        var (db, _, _, _, _) = Db();
        await SalidaAppAlmacenes.RegistrarAsync(db, new Eventos(), EmpresaId, Usuario, Salida(2, 85.5m, TipoMovimiento.AjusteNegativo), default);
        await db.SaveChangesAsync();

        var mov = await db.Movimientos.Include(m => m.Lineas).SingleAsync();
        mov.Tipo.Should().Be(TipoMovimiento.AjusteNegativo);
        mov.Lineas.Single().CostoUnitarioMxn.Should().Be(85.5m);
    }

    [Fact]
    public async Task Sin_existencia_suficiente_queda_en_revision_y_no_mueve_saldo()
    {
        var (db, _, _, _, _) = Db();
        var (estado, folio, error) = await SalidaAppAlmacenes.RegistrarAsync(db, new Eventos(), EmpresaId, Usuario, Salida(11), default);
        await db.SaveChangesAsync();

        estado.Should().Be(SalidaAppAlmacenes.EnRevision);
        folio.Should().BeNull();
        error.Should().Contain("disponible 10").And.Contain("solicitada 11");
        (await db.Movimientos.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Reparte_entre_sub_almacenes_con_un_movimiento_por_cada_uno()
    {
        var (db, _, _, subB, ubiB) = Db();
        db.SaldosInventario.Add(new SaldoInventario(ubiB, subB, Articulo, 4, 120m, Variante));
        await db.SaveChangesAsync();

        var (estado, folio, _) = await SalidaAppAlmacenes.RegistrarAsync(db, new Eventos(), EmpresaId, Usuario, Salida(12), default);
        await db.SaveChangesAsync();

        estado.Should().Be(SalidaAppAlmacenes.Recibido);
        folio!.Split(", ").Should().HaveCount(2);
        var lineas = await db.LineasMovimiento.ToListAsync();
        lineas.Sum(l => l.Cantidad).Should().Be(12);
        lineas.Should().Contain(l => l.Cantidad == 10 && l.CostoUnitarioMxn == 100m)
            .And.Contain(l => l.Cantidad == 2 && l.CostoUnitarioMxn == 120m);
    }

    [Fact]
    public async Task Dos_partidas_del_mismo_articulo_cuya_suma_excede_la_existencia_quedan_en_revision_sin_movimientos()
    {
        var (db, _, _, _, _) = Db();
        var (estado, folio, error) = await SalidaAppAlmacenes.RegistrarAsync(
            db, new Eventos(), EmpresaId, Usuario, [Salida(6), Salida(6)], default);
        await db.SaveChangesAsync();

        estado.Should().Be(SalidaAppAlmacenes.EnRevision);
        folio.Should().BeNull();
        error.Should().Contain("disponible 10").And.Contain("solicitada 12");
        (await db.Movimientos.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Dos_partidas_del_mismo_articulo_dentro_de_existencia_no_toman_dos_veces_el_mismo_saldo()
    {
        var (db, _, ubiA, subB, ubiB) = Db();
        db.SaldosInventario.Add(new SaldoInventario(ubiB, subB, Articulo, 4, 120m, Variante)); // 10 en A + 4 en B
        await db.SaveChangesAsync();

        var (estado, _, _) = await SalidaAppAlmacenes.RegistrarAsync(
            db, new Eventos(), EmpresaId, Usuario, [Salida(8, 90m), Salida(5, 110m)], default);
        await db.SaveChangesAsync();

        estado.Should().Be(SalidaAppAlmacenes.Recibido);
        var lineas = await db.LineasMovimiento.ToListAsync();
        lineas.Sum(l => l.Cantidad).Should().Be(13);
        lineas.Sum(l => l.Cantidad * l.CostoUnitarioMxn).Should().Be(8 * 90m + 5 * 110m);
        // Ninguna ubicación sale por más de su saldo: A (10) se agota entre ambas partidas, B (4) aporta solo 3.
        lineas.Where(l => l.UbicacionId == ubiA).Sum(l => l.Cantidad).Should().Be(10);
        lineas.Where(l => l.UbicacionId == ubiB).Sum(l => l.Cantidad).Should().Be(3);
    }

    [Fact]
    public async Task Dos_articulos_distintos_uno_corto_deja_todo_en_revision_sin_movimientos()
    {
        var (db, subA, ubiA, _, _) = Db();
        var otro = Guid.NewGuid();
        db.SaldosInventario.Add(new SaldoInventario(ubiA, subA, otro, 2, 50m));
        await db.SaveChangesAsync();

        var corto = new SalidaApp(TipoMovimiento.SalidaConsumo, Sucursal, otro, "PZA", null, 3, null, "App Almacenes AJUSTE_SALIDA");
        var (estado, folio, error) = await SalidaAppAlmacenes.RegistrarAsync(
            db, new Eventos(), EmpresaId, Usuario, [Salida(3), corto], default);
        await db.SaveChangesAsync();

        estado.Should().Be(SalidaAppAlmacenes.EnRevision);
        folio.Should().BeNull();
        error.Should().Contain("disponible 2").And.Contain("solicitada 3");
        (await db.Movimientos.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Rotura_de_una_hoja_publica_un_evento_a_costo_con_motivo_sucursal_y_almacen()
    {
        var (db, _, _, _, _) = Db();
        var eventos = new Eventos();
        var rotura = new SalidaApp(TipoMovimiento.BajaPorDano, Sucursal, Articulo, "PZA", Variante, 1, 1200m,
            "App Almacenes ROTURA: se quebró en el corte", 2440m, 3660m);
        await SalidaAppAlmacenes.RegistrarAsync(db, eventos, EmpresaId, Usuario, rotura, default);
        await db.SaveChangesAsync();

        var ev = eventos.Publicados.OfType<SalidaInventarioRegistradaIntegrationEvent>().Should().ContainSingle().Subject;
        ev.MontoTotalMxn.Should().Be(1200m);
        ev.Causa.Should().Be("rotura");
        ev.Motivo.Should().Contain("ROTURA");
        ev.SucursalId.Should().Be(Sucursal);
        ev.AlmacenId.Should().NotBeNull();
        ev.FolioMovimiento.Should().StartWith("M-BAJ");
        ev.Lineas.Should().ContainSingle(l => l.ArticuloId == Articulo && l.Cantidad == 1 && l.MontoMxn == 1200m);
    }

    [Fact]
    public async Task H1_el_area_o_equipo_capturado_va_en_cada_linea_y_en_el_evento()
    {
        var (db, _, _, _, _) = Db();
        var eventos = new Eventos();
        var centro = Guid.NewGuid();
        await SalidaAppAlmacenes.RegistrarAsync(db, eventos, EmpresaId, Usuario, Salida(3) with { CentroCostoId = centro }, default);
        await db.SaveChangesAsync();

        (await db.Movimientos.Include(m => m.Lineas).SingleAsync()).Lineas.Should().OnlyContain(l => l.CentroCostoId == centro);
        var ev = eventos.Publicados.OfType<SalidaInventarioRegistradaIntegrationEvent>().Should().ContainSingle().Subject;
        ev.CentroCostoId.Should().Be(centro);
        ev.Lineas.Should().OnlyContain(l => l.CentroCostoId == centro);
    }

    [Fact]
    public async Task H1_sin_area_el_evento_sigue_sin_centro()
    {
        var (db, _, _, _, _) = Db();
        var eventos = new Eventos();
        await SalidaAppAlmacenes.RegistrarAsync(db, eventos, EmpresaId, Usuario, Salida(3), default);
        await db.SaveChangesAsync();

        eventos.Publicados.OfType<SalidaInventarioRegistradaIntegrationEvent>().Single().CentroCostoId.Should().BeNull();
    }

    [Fact]
    public async Task Sin_existencia_no_publica_evento_porque_no_hay_movimiento()
    {
        var (db, _, _, _, _) = Db();
        var eventos = new Eventos();
        await SalidaAppAlmacenes.RegistrarAsync(db, eventos, EmpresaId, Usuario, Salida(11), default);
        eventos.Publicados.Should().BeEmpty();
    }

    [Fact]
    public async Task Una_salida_con_dos_tipos_publica_un_evento_por_movimiento()
    {
        var (db, _, _, _, _) = Db();
        var eventos = new Eventos();
        await SalidaAppAlmacenes.RegistrarAsync(db, eventos, EmpresaId, Usuario,
            [Salida(1, tipo: TipoMovimiento.SalidaConsumo), Salida(2, 85.5m, TipoMovimiento.AjusteNegativo)], default);
        await db.SaveChangesAsync();

        var causas = eventos.Publicados.OfType<SalidaInventarioRegistradaIntegrationEvent>().Select(e => e.Causa).ToList();
        causas.Should().BeEquivalentTo("consumo", "salida_manual");
    }

    private sealed class FakeEmpresa(Guid current) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = current;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}

/// <summary>Publicador de prueba: guarda lo que se encoló.</summary>
public sealed class Eventos : IIntegrationEventPublisher
{
    public List<object> Publicados { get; } = [];
    public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken)
    {
        Publicados.Add(integrationEvent);
        return Task.CompletedTask;
    }
}
