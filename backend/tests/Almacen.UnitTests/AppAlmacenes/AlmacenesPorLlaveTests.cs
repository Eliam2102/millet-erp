using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Infrastructure.Persistence.Migrations;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.AppAlmacenes;

/// <summary>
/// M-446 (InMemory): llaves sucursal + tipo + línea de la app → ubicación del ERP; las salidas toman solo de su llave y el
/// CAMBIO_ALMACEN mueve origen → destino con un par enlazado, al costo del origen, conservando medida y lote, sin evento contable.
/// El saldo lo mueve el trigger de Postgres (prueba de integración); aquí se verifican los movimientos que se registran.
/// </summary>
public class AlmacenesPorLlaveTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly Guid Sucursal = Guid.NewGuid();
    private static readonly Guid Articulo = Guid.NewGuid();
    private static readonly Guid Variante = Guid.NewGuid();
    private const decimal Hoja = 20.1422m; // m² de la hoja 3302 × 6100

    private static readonly LlaveAlmacenApp ExpHegla1 = new("EXPORTACION", "HEGLA 1");
    private static readonly LlaveAlmacenApp ExpBystronic = new("EXPORTACION", "BYSTRONIC");

    /// <summary>Sucursal con las 16 llaves de la lista de TI (una ubicación por llave, un sub-almacén) y 25 hojas en EXPORTACION · HEGLA 1.</summary>
    private static (AlmacenDbContext Db, Dictionary<string, Guid> Ubi, Guid Sub) Db(decimal hojasOrigen = 25)
    {
        var db = new AlmacenDbContext(new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"llaves-app-{Guid.NewGuid():N}").Options, new FakeEmpresa(EmpresaId));
        db.Database.EnsureCreated();
        var alm = Guid.NewGuid(); var sub = Guid.NewGuid();
        db.Almacenes.Add(new Domain.Catalogo.Almacen(alm, "APP-CONKAL", "App", Sucursal));
        db.SubAlmacenes.Add(new SubAlmacen(sub, alm, "MP-APP", "MP app", TipoSubAlmacen.MaterialesDirectos));
        var ubi = new Dictionary<string, Guid>();
        foreach (var k in M446AlmacenesAppLlaves.Llaves)
        {
            var id = Guid.NewGuid();
            ubi[$"{k.Tipo}|{k.Linea}"] = id;
            db.Ubicaciones.Add(new Ubicacion(id, sub, k.Clave, k.Nombre));
            db.AlmacenesApp.Add(new AlmacenApp(Guid.NewGuid(), Sucursal, k.Tipo, k.Linea, id, k.UbicacionAw));
        }
        db.SaldosInventario.Add(new SaldoInventario(ubi["EXPORTACION|HEGLA 1"], sub, Articulo, hojasOrigen * Hoja, 285.4m, Variante, "L-1"));
        db.SaveChanges();
        return (db, ubi, sub);
    }

    private static SalidaApp Hojas(decimal hojas, LlaveAlmacenApp? llave, TipoMovimiento tipo = TipoMovimiento.TrasladoInterno, string? lote = "L-1") =>
        new(tipo, Sucursal, Articulo, "M2", Variante, hojas * Hoja, null, "App Almacenes CAMBIO_ALMACEN MV-1", 3302m, 6100m,
            ArticuloAw: "100006", Almacen: llave, Lote: lote);

    // ── Criterio 1: las 16 llaves ──────────────────────────────────────────────────────────────

    [Fact]
    public void La_lista_de_TI_son_16_llaves_distintas_con_clave_de_ubicacion_valida()
    {
        var llaves = M446AlmacenesAppLlaves.Llaves;
        llaves.Should().HaveCount(16);
        llaves.Select(k => (k.Tipo, k.Linea)).Should().OnlyHaveUniqueItems();
        llaves.Select(k => k.Clave).Should().OnlyHaveUniqueItems().And.OnlyContain(c => c.Length <= 20);
        llaves.Count(k => k.Linea == "").Should().Be(4, "una llave de herraje sin línea por división");
        llaves.Single(k => k.Tipo == "IMPORTACION TEMPORAL" && k.Linea == "HEGLA 2").UbicacionAw.Should().Be("CRISTAL - HEGLA 2");
    }

    [Fact]
    public void La_carga_es_idempotente_por_construccion()
    {
        var sql = M446AlmacenesAppLlaves.SembrarSql("conkal");
        sql.Should().Contain("'APP-CONKAL'");
        // Cada INSERT se apoya en una llave única (almacén por clave, sub por almacén+clave, ubicación por sub+clave, llave por
        // sucursal+tipo+línea): la segunda corrida no inserta nada. La prueba contra Postgres la corre dos veces.
        System.Text.RegularExpressions.Regex.Count(sql, "INSERT INTO")
            .Should().Be(System.Text.RegularExpressions.Regex.Count(sql, "ON CONFLICT DO NOTHING"));
    }

    // ── Resolución de la llave ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task La_llave_se_resuelve_sin_importar_mayusculas_ni_espacios()
    {
        var (db, ubi, sub) = Db();
        var (mapa, error) = await LlavesAlmacenApp.ResolverAsync(db, Sucursal, [new LlaveAlmacenApp(" importacion  temporal ", "hegla 2"),
            new LlaveAlmacenApp("OBRAS", null)], default);

        error.Should().BeNull();
        mapa![new LlaveAlmacenApp("IMPORTACION TEMPORAL", "HEGLA 2")].Should().Be(new UbicacionApp(ubi["IMPORTACION TEMPORAL|HEGLA 2"], sub, mapa.Values.First().AlmacenId));
        mapa[new LlaveAlmacenApp("OBRAS", "")].UbicacionId.Should().Be(ubi["OBRAS|"]);
    }

    [Fact]
    public async Task Llave_desconocida_o_sin_tipo_en_sucursal_con_llaves_da_error()
    {
        var (db, _, _) = Db();
        (await LlavesAlmacenApp.ResolverAsync(db, Sucursal, [new LlaveAlmacenApp("EXPORTACION", "HEGLA 3")], default))
            .Error.Should().Contain("EXPORTACION HEGLA 3").And.Contain("no está en la lista");
        (await LlavesAlmacenApp.ResolverAsync(db, Sucursal, [ExpHegla1, null], default))
            .Error.Should().Contain("Falta almacenGeneral.tipo");
    }

    [Fact]
    public async Task Sin_tipo_en_el_mensaje_o_sucursal_sin_llaves_es_el_comportamiento_previo()
    {
        var (db, _, _) = Db();
        (await LlavesAlmacenApp.ResolverAsync(db, Sucursal, [null, null], default)).Should().Be(((IReadOnlyDictionary<LlaveAlmacenApp, UbicacionApp>?)null, (string?)null));
        (await LlavesAlmacenApp.ResolverAsync(db, Guid.NewGuid(), [ExpHegla1], default)).Mapa.Should().BeNull();
    }

    // ── Salidas por llave ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task La_salida_con_llave_solo_toma_de_la_ubicacion_de_su_llave()
    {
        var (db, ubi, _) = Db();
        var (estado, _, _) = await SalidaAppAlmacenes.RegistrarAsync(db, new Eventos(), EmpresaId, Usuario,
            Hojas(2, ExpHegla1, TipoMovimiento.AjusteNegativo), default);
        estado.Should().Be(SalidaAppAlmacenes.Recibido);
        db.ChangeTracker.Entries<MovimientoInventario>().Single().Entity.Lineas.Single().UbicacionId.Should().Be(ubi["EXPORTACION|HEGLA 1"]);

        var (otra, _, error) = await SalidaAppAlmacenes.RegistrarAsync(db, new Eventos(), EmpresaId, Usuario,
            Hojas(2, ExpBystronic, TipoMovimiento.AjusteNegativo), default);
        otra.Should().Be(SalidaAppAlmacenes.EnRevision, "BYSTRONIC no tiene existencia aunque HEGLA 1 sí");
        error.Should().Contain("EXPORTACION BYSTRONIC");
    }

    [Fact]
    public async Task La_salida_con_llave_desconocida_da_ERROR_sin_movimiento()
    {
        var (db, _, _) = Db();
        var (estado, _, error) = await SalidaAppAlmacenes.RegistrarAsync(db, new Eventos(), EmpresaId, Usuario,
            Hojas(1, new LlaveAlmacenApp("NACIONAL", "HEGLA 1"), TipoMovimiento.AjusteNegativo), default);

        estado.Should().Be("ERROR");
        error.Should().Contain("NACIONAL HEGLA 1");
        db.ChangeTracker.Entries<MovimientoInventario>().Should().BeEmpty();
    }

    // ── Criterios 2 y 3: CAMBIO_ALMACEN ────────────────────────────────────────────────────────

    [Fact]
    public async Task Diez_hojas_HEGLA_1_a_BYSTRONIC_es_un_par_enlazado_al_costo_del_origen_con_su_lote_y_un_folio()
    {
        var (db, ubi, _) = Db();
        var (estado, folio, error) = await TrasladoAppAlmacenes.RegistrarAsync(db, EmpresaId, Usuario, [Hojas(10, ExpHegla1)],
            ExpBystronic, default);
        await db.SaveChangesAsync();

        (estado, error).Should().Be((SalidaAppAlmacenes.Recibido, (string?)null));
        folio.Should().StartWith("M-TRA").And.NotContain(",");
        var movs = await db.Movimientos.Include(m => m.Lineas).ToListAsync();
        var salida = movs.Single(m => m.Tipo == TipoMovimiento.TrasladoInterno);
        var entrada = movs.Single(m => m.Tipo == TipoMovimiento.EntradaTrasladoInterno);
        salida.Folio.Should().Be(folio);
        entrada.SalidaOrigenId.Should().Be(salida.Id);
        var (ls, le) = (salida.Lineas.Single(), entrada.Lineas.Single());
        (ls.UbicacionId, le.UbicacionId).Should().Be((ubi["EXPORTACION|HEGLA 1"], ubi["EXPORTACION|BYSTRONIC"]));
        (ls.Cantidad, le.Cantidad).Should().Be((10 * Hoja, 10 * Hoja));
        (le.CostoUnitarioMxn, le.Lote, le.ProductoAwVarianteId, le.AnchoMm, le.AltoMm).Should().Be((285.4m, "L-1", Variante, 3302m, 6100m));
        TipoMovimiento.TrasladoInterno.EsTraslado().Should().BeTrue();
        TipoMovimiento.EntradaTrasladoInterno.EsEntrada().Should().BeTrue();
        TipoMovimiento.TrasladoInterno.EsEntrada().Should().BeFalse();
    }

    [Fact]
    public async Task Treinta_hojas_con_25_en_el_origen_queda_en_revision_y_no_mueve_nada()
    {
        var (db, _, _) = Db();
        var (estado, folio, error) = await TrasladoAppAlmacenes.RegistrarAsync(db, EmpresaId, Usuario, [Hojas(30, ExpHegla1)],
            ExpBystronic, default);

        (estado, folio).Should().Be((SalidaAppAlmacenes.EnRevision, (string?)null));
        error.Should().Contain("Sin existencia suficiente").And.Contain("EXPORTACION HEGLA 1");
        db.ChangeTracker.Entries<MovimientoInventario>().Should().BeEmpty();
    }

    [Fact]
    public async Task Destino_fuera_de_la_lista_da_ERROR_sin_movimiento()
    {
        var (db, _, _) = Db();
        var (estado, _, error) = await TrasladoAppAlmacenes.RegistrarAsync(db, EmpresaId, Usuario, [Hojas(1, ExpHegla1)],
            new LlaveAlmacenApp("OBRAS", "HEGLA 9"), default);

        estado.Should().Be("ERROR");
        error.Should().Contain("OBRAS HEGLA 9");
        db.ChangeTracker.Entries<MovimientoInventario>().Should().BeEmpty();
    }

    [Fact]
    public async Task Toma_primero_el_lote_del_mensaje()
    {
        var (db, ubi, sub) = Db();
        db.SaldosInventario.Add(new SaldoInventario(ubi["EXPORTACION|HEGLA 1"], sub, Articulo, 40 * Hoja, 285.4m, Variante, "L-GRANDE"));
        await db.SaveChangesAsync();

        await TrasladoAppAlmacenes.RegistrarAsync(db, EmpresaId, Usuario, [Hojas(5, ExpHegla1, lote: "L-1")], ExpBystronic, default);

        db.ChangeTracker.Entries<MovimientoInventario>().SelectMany(e => e.Entity.Lineas).Select(l => l.Lote).Should().OnlyContain(l => l == "L-1");
    }

    // ── Entrada por llave ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ENTRADA_ALMACEN_con_llave_entra_a_la_ubicacion_de_su_llave()
    {
        var (db, ubi, _) = Db();
        var puertos = new EntradaAppPuertos(null!, null!, new PeriodoAbierto(), new Eventos());
        var (estado, _, error) = await EntradaAppAlmacenes.RegistrarAsync(db, puertos, EmpresaId, Usuario, Sucursal, "m", null, "EN-1",
            [new EntradaApp(Articulo, "M2", Variante, 2 * Hoja, 3302m, 6100m, "L-2", new LlaveAlmacenApp("EXPORTACION", "HEGLA 1"))], default);

        (estado, error).Should().Be((SalidaAppAlmacenes.Recibido, (string?)null));
        db.ChangeTracker.Entries<MovimientoInventario>().Single().Entity.Lineas.Single().UbicacionId.Should().Be(ubi["EXPORTACION|HEGLA 1"]);
    }

    private sealed class PeriodoAbierto : Millet.Almacen.Domain.Ports.IPeriodoContableReadPort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class Eventos : IIntegrationEventPublisher
    {
        public Task PublishAsync(object integrationEvent, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeEmpresa(Guid current) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = current;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }
}
