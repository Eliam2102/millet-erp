using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.UnitTests.AppAlmacenes;

/// <summary>
/// M-159 (InMemory): ENTRADA_ALMACEN entra al almacén general de la sucursal, sin rack. Contra una OC del ERP es su
/// recepción: costo de la línea de la OC, pendiente exacto, evento al outbox; si no, promedio vigente y nunca costo cero.
/// </summary>
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

    private static EntradaApp Hojas(decimal hojas, string? lote = "L-VPS-2611-01") =>
        new(Articulo, "M2", Variante, hojas * 20.1422m, 3302m, 6100m, lote);

    // ── Lote (Eliam E3, ficha MP-05): llave del saldo; obligatorio en el vidrio de A+W ──────────

    [Fact]
    public async Task El_lote_del_grupo_llega_a_la_linea_del_movimiento()
    {
        var (db, _, _) = Db();
        await EntradaAppAlmacenes.RegistrarAsync(db, Puertos(Oc()), EmpresaId, Usuario, Sucursal, "m", FolioOc, "EN-L1", [Hojas(2)], default);
        await db.SaveChangesAsync();

        (await db.LineasMovimiento.SingleAsync()).Lote.Should().Be("L-VPS-2611-01");
    }

    [Fact]
    public async Task Hoja_de_AW_sin_lote_da_ERROR_sin_movimiento()
    {
        var (db, _, _) = Db();
        var eventos = new Captura();
        var (estado, folio, error) = await EntradaAppAlmacenes.RegistrarAsync(db, Puertos(Oc(), eventos), EmpresaId, Usuario, Sucursal,
            "m", FolioOc, "EN-L2", [Hojas(1, "  ")], default);

        (estado, folio).Should().Be(("ERROR", (string?)null));
        error.Should().Contain("Falta el lote").And.Contain("3302 × 6100");
        db.ChangeTracker.Entries<MovimientoInventario>().Should().BeEmpty();
        eventos.Publicados.Should().BeEmpty();
    }

    [Fact]
    public async Task Dos_lotes_en_una_linea_dan_dos_renglones_por_lote()
    {
        var (db, _, _) = Db();
        await EntradaAppAlmacenes.RegistrarAsync(db, Puertos(Oc()), EmpresaId, Usuario, Sucursal, "m", FolioOc, "EN-L3",
            [Hojas(3, "L-A"), Hojas(2, "L-B")], default);
        await db.SaveChangesAsync();

        var lineas = await db.LineasMovimiento.OrderBy(l => l.Posicion).ToListAsync();
        lineas.Select(l => (l.Lote, l.Cantidad)).Should().Equal(("L-A", 3 * 20.1422m), ("L-B", 2 * 20.1422m));
    }

    [Fact]
    public async Task Sin_medida_el_lote_es_opcional()
    {
        var (db, unica, sub) = Db();
        var herraje = Guid.NewGuid();
        db.SaldosInventario.Add(new SaldoInventario(unica, sub, herraje, 1m, 12m));
        await db.SaveChangesAsync();

        var (estado, _, error) = await EntradaAppAlmacenes.RegistrarAsync(db, Puertos(), EmpresaId, Usuario, Sucursal, "m", null, "EN-L4",
            [new EntradaApp(herraje, "PZA", null, 4m)], default);

        (estado, error).Should().Be((SalidaAppAlmacenes.Recibido, (string?)null));
    }

    [Fact]
    public async Task Diez_hojas_3302x6100_entran_al_almacen_general_sin_rack_con_un_folio_ENT()
    {
        var (db, unica, sub) = Db();
        db.SaldosInventario.Add(new SaldoInventario(unica, sub, Articulo, 20.1422m, 100m, Variante));
        await db.SaveChangesAsync();
        var (estado, folio, error) = await EntradaAppAlmacenes.RegistrarAsync(
            db, Puertos(), EmpresaId, Usuario, Sucursal, "App Almacenes ENTRADA_ALMACEN EN-1", "zz_arnes_EKH", "EN-1", [Hojas(10)], default);
        await db.SaveChangesAsync();

        estado.Should().Be(SalidaAppAlmacenes.Recibido);
        error.Should().BeNull();
        folio.Should().StartWith("M-ENT");
        var mov = await db.Movimientos.Include(m => m.Lineas).SingleAsync();
        mov.Tipo.Should().Be(TipoMovimiento.EntradaCompra);
        mov.Motivo.Should().Be("App Almacenes ENTRADA_ALMACEN EN-1");
        mov.OcId.Should().BeNull();
        mov.Lineas.Should().ContainSingle(l => l.Cantidad == 201.422m && l.UbicacionId == unica
            && l.ProductoAwVarianteId == Variante && l.AnchoMm == 3302m && l.AltoMm == 6100m);
    }

    [Fact]
    public async Task Con_saldo_previo_entra_al_promedio_vigente_para_no_moverlo()
    {
        var (db, unica, sub) = Db();
        db.SaldosInventario.Add(new SaldoInventario(unica, sub, Articulo, 50m, 210.5m, Variante));
        await db.SaveChangesAsync();

        await EntradaAppAlmacenes.RegistrarAsync(db, Puertos(), EmpresaId, Usuario, Sucursal, "m", null, "EN-1", [Hojas(1)], default);
        await db.SaveChangesAsync();

        (await db.LineasMovimiento.SingleAsync()).CostoUnitarioMxn.Should().Be(210.5m);
    }

    [Fact]
    public async Task Sucursal_sin_almacen_general_da_ERROR_sin_movimiento()
    {
        var (db, _, _) = Db();
        var (estado, folio, error) = await EntradaAppAlmacenes.RegistrarAsync(
            db, Puertos(), EmpresaId, Usuario, Guid.NewGuid(), "m", null, "EN-1", [Hojas(1)], default);

        estado.Should().Be("ERROR");
        folio.Should().BeNull();
        error.Should().Contain("almacén general");
        (await db.Movimientos.CountAsync()).Should().Be(0);
    }

    // ── Contra una OC del ERP: es su recepción ──────────────────────────────────

    private static readonly Guid OcId = Guid.NewGuid();
    private static readonly Guid LineaOc = Guid.NewGuid();
    private const string FolioOc = "OC-CNK2026-000123";

    /// <summary>OC autorizada a la sucursal: 10 hojas 3302×6100 (201.422 m²) a 285.40 MXN/m².</summary>
    private static OcLectura Oc(string estado = "Autorizada", decimal recibidaCompras = 0m) => new(
        OcId, FolioOc, Guid.NewGuid(), EmpresaId, estado,
        [new OcLineaLectura(LineaOc, Articulo, "M2", 201.422m, recibidaCompras, 285.40m, ProductoAwVarianteId: Variante, AnchoMm: 3302m, AltoMm: 6100m)],
        SucursalDestinoId: Sucursal);

    [Fact]
    public async Task Contra_OC_entra_al_precio_de_la_linea_ligada_y_publica_la_recepcion_con_factura_pendiente()
    {
        var (db, unica, sub) = Db();
        // Saldo previo a otro costo: el promedio NO manda, manda la OC.
        db.SaldosInventario.Add(new SaldoInventario(unica, sub, Articulo, 20.1422m, 95.13m, Variante));
        await db.SaveChangesAsync();
        var eventos = new Captura();

        var (estado, folio, error) = await EntradaAppAlmacenes.RegistrarAsync(
            db, Puertos(Oc(), eventos), EmpresaId, Usuario, Sucursal, "m", FolioOc, "EN-000990", [Hojas(4)], default);
        await db.SaveChangesAsync();

        (estado, error).Should().Be((SalidaAppAlmacenes.Recibido, (string?)null));
        var mov = await db.Movimientos.Include(m => m.Lineas).SingleAsync();
        mov.Folio.Should().Be(folio);
        mov.OcId.Should().Be(OcId);
        mov.PackingListBlobRef.Should().Be("app-almacenes:EN-000990");
        var linea = mov.Lineas.Single();
        (linea.CostoUnitarioMxn, linea.LineaOcId, linea.UbicacionId).Should().Be((285.40m, (Guid?)LineaOc, (Guid?)unica));
        var e = eventos.Publicados.OfType<OcRecepcionRegistradaIntegrationEvent>().Single();
        (e.OrdenCompraId, e.FacturaPendiente, e.RecepcionId, e.SucursalId).Should().Be((OcId, true, mov.Id, (Guid?)Sucursal));
        var l = e.Lineas.Single();
        (l.LineaOcId, l.Cantidad, l.CostoUnitarioMxn, l.CantidadRecibidaAcumulada).Should().Be(((Guid?)LineaOc, 4 * 20.1422m, 285.40m, (decimal?)(4 * 20.1422m)));
    }

    [Fact]
    public async Task Contra_OC_la_misma_entrada_otra_vez_es_DUPLICADO_sin_segunda_recepcion()
    {
        var (db, _, _) = Db();
        var eventos = new Captura();
        var (_, folio, _) = await EntradaAppAlmacenes.RegistrarAsync(db, Puertos(Oc(), eventos), EmpresaId, Usuario, Sucursal, "m", FolioOc, "EN-7", [Hojas(1)], default);
        await db.SaveChangesAsync();

        var otra = await EntradaAppAlmacenes.RegistrarAsync(db, Puertos(Oc(), eventos), EmpresaId, Usuario, Sucursal, "m", FolioOc, "EN-7", [Hojas(1)], default);

        otra.Should().Be(("DUPLICADO", folio, (string?)null));
        (await db.Movimientos.CountAsync()).Should().Be(1);
        eventos.Publicados.Should().ContainSingle();
    }

    [Fact]
    public async Task Contra_OC_mas_de_lo_pendiente_da_ERROR_sin_movimiento()
    {
        var (db, _, _) = Db();
        var eventos = new Captura();
        // Compras ya vio 8 de 10 hojas; llegan 3.
        var (estado, _, error) = await EntradaAppAlmacenes.RegistrarAsync(db, Puertos(Oc(recibidaCompras: 8 * 20.1422m), eventos),
            EmpresaId, Usuario, Sucursal, "m", FolioOc, "EN-8", [Hojas(3)], default);

        estado.Should().Be("ERROR");
        error.Should().Contain("Excede lo pendiente").And.Contain("2 hojas");
        db.ChangeTracker.Entries<MovimientoInventario>().Should().BeEmpty();
        eventos.Publicados.Should().BeEmpty();
    }

    [Fact]
    public async Task Contra_OC_no_autorizada_da_ERROR()
    {
        var (db, _, _) = Db();
        var (estado, _, error) = await EntradaAppAlmacenes.RegistrarAsync(db, Puertos(Oc("Cerrada")),
            EmpresaId, Usuario, Sucursal, "m", FolioOc, "EN-9", [Hojas(1)], default);

        estado.Should().Be("ERROR");
        error.Should().Contain("no se puede recibir");
        db.ChangeTracker.Entries<MovimientoInventario>().Should().BeEmpty();
    }

    [Fact]
    public async Task Contra_OC_medida_que_no_esta_en_la_OC_da_ERROR()
    {
        var (db, _, _) = Db();
        var otra = new EntradaApp(Articulo, "M2", Guid.NewGuid(), 20m, 2440m, 3660m, "L-1");
        var (estado, _, error) = await EntradaAppAlmacenes.RegistrarAsync(db, Puertos(Oc()),
            EmpresaId, Usuario, Sucursal, "m", FolioOc, "EN-10", [otra], default);

        estado.Should().Be("ERROR");
        error.Should().Contain(FolioOc).And.Contain("2440 × 3660");
    }

    [Fact]
    public async Task Sin_OC_del_ERP_y_sin_promedio_da_ERROR_nunca_costo_cero()
    {
        var (db, _, _) = Db();
        var (estado, _, error) = await EntradaAppAlmacenes.RegistrarAsync(db, Puertos(), EmpresaId, Usuario, Sucursal, "m",
            "zz_arnes_EKH", "EN-11", [Hojas(1)], default);

        estado.Should().Be("ERROR");
        error.Should().Contain("zz_arnes_EKH").And.Contain("costo cero");
        db.ChangeTracker.Entries<MovimientoInventario>().Should().BeEmpty();
    }

    [Fact]
    public async Task Periodo_contable_no_abierto_da_ERROR_sin_movimiento()
    {
        var (db, _, _) = Db();
        var (estado, _, error) = await EntradaAppAlmacenes.RegistrarAsync(db, Puertos(Oc(), periodoAbierto: false),
            EmpresaId, Usuario, Sucursal, "m", FolioOc, "EN-12", [Hojas(1)], default);

        estado.Should().Be("ERROR");
        error.Should().Contain("no está abierto en Contabilidad");
        db.ChangeTracker.Entries<MovimientoInventario>().Should().BeEmpty();
    }

    // ── ENTRADA_MANUAL (M-283, contrato v9): ajuste positivo al promedio del ERP ─────────────────────

    [Fact]
    public async Task Entrada_manual_entra_al_promedio_del_ERP_como_AJP_aunque_traiga_costo_de_AW_y_compra()
    {
        var (db, unica, sub) = Db();
        db.SaldosInventario.Add(new SaldoInventario(unica, sub, Articulo, 20.1422m, 210.5m, Variante));
        await db.SaveChangesAsync();
        var eventos = new Captura();

        var (estado, folio, error) = await EntradaAppAlmacenes.RegistrarAsync(db, Puertos(Oc(), eventos), EmpresaId, Usuario, Sucursal,
            "App Almacenes ENTRADA_MANUAL MV-1", FolioOc, "msg-1", [Hojas(2) with { CostoRespaldoMxn = 130m }], default,
            TipoMovimiento.AjustePositivo);
        await db.SaveChangesAsync();

        (estado, error).Should().Be((SalidaAppAlmacenes.Recibido, (string?)null));
        folio.Should().StartWith("M-AJP");
        var mov = await db.Movimientos.Include(m => m.Lineas).SingleAsync();
        (mov.Tipo, mov.OcId).Should().Be((TipoMovimiento.AjustePositivo, (Guid?)null)); // nunca es recepción de OC
        mov.Lineas.Single().CostoUnitarioMxn.Should().Be(210.5m);
        eventos.Publicados.Should().BeEmpty();
    }

    [Fact]
    public async Task Entrada_manual_sin_promedio_del_ERP_entra_al_costo_de_AW_del_mensaje()
    {
        var (db, _, _) = Db();
        await EntradaAppAlmacenes.RegistrarAsync(db, Puertos(), EmpresaId, Usuario, Sucursal, "m", null, "msg-2",
            [Hojas(1) with { CostoRespaldoMxn = 130m }], default, TipoMovimiento.AjustePositivo);
        await db.SaveChangesAsync();

        (await db.LineasMovimiento.SingleAsync()).CostoUnitarioMxn.Should().Be(130m);
    }

    [Fact]
    public async Task Entrada_manual_sin_promedio_ni_costo_da_ERROR_sin_movimiento()
    {
        var (db, _, _) = Db();
        var (estado, _, error) = await EntradaAppAlmacenes.RegistrarAsync(db, Puertos(), EmpresaId, Usuario, Sucursal, "m", null, "msg-3",
            [Hojas(1)], default, TipoMovimiento.AjustePositivo);

        estado.Should().Be("ERROR");
        error.Should().StartWith("Entrada manual").And.Contain("costo cero");
        db.ChangeTracker.Entries<MovimientoInventario>().Should().BeEmpty();
    }

    // ── origen (M-283, contrato v9): entra o sale al costo del movimiento original ─────────────────────

    /// <summary>Registra un movimiento como lo haría el mensaje <paramref name="referencia"/> y deja su MensajeApp aplicado.</summary>
    private static async Task AplicadoAsync(AlmacenDbContext db, string tipo, string referencia, string? folio)
    {
        await db.SaveChangesAsync();
        db.MensajesApp.Add(new Domain.Idempotencia.MensajeApp(Guid.NewGuid().ToString(), tipo, referencia, "{}",
            SalidaAppAlmacenes.Recibido, folio, null));
        await db.SaveChangesAsync();
    }

    private static SalidaApp Prestamo(decimal m2, decimal? costo, string lote = "L-1") =>
        new(TipoMovimiento.AjusteNegativo, Sucursal, Articulo, "M2", Variante, m2, costo, "App Almacenes SALIDA_MANUAL MV-80: Préstamo",
            3302m, 6100m, Lote: lote);

    [Fact]
    public async Task Regreso_de_prestamo_entra_al_costo_con_que_salio_aunque_el_promedio_sea_otro()
    {
        var (db, unica, sub) = Db();
        db.SaldosInventario.Add(new SaldoInventario(unica, sub, Articulo, 100m, 100m, Variante, "L-1"));
        await db.SaveChangesAsync();
        // El préstamo salió a 150 (p. ej. el promedio de ese día); hoy el promedio del saldo es 100.
        var (_, folioSalida, _) = await SalidaAppAlmacenes.RegistrarAsync(db, new Captura(), EmpresaId, Usuario, [Prestamo(2 * 20.1422m, 150m)], default);
        await AplicadoAsync(db, "SALIDA_MANUAL", "MV-80", folioSalida);

        var (estado, _, error) = await EntradaAppAlmacenes.RegistrarAsync(db, Puertos(), EmpresaId, Usuario, Sucursal, "m", null, "msg-r",
            [Hojas(1, "L-1") with { CostoRespaldoMxn = 130m }], default, TipoMovimiento.AjustePositivo,
            new OrigenApp("REGRESO_PRESTAMO", "MV-80"));
        await db.SaveChangesAsync();

        (estado, error).Should().Be((SalidaAppAlmacenes.Recibido, (string?)null));
        (await db.Movimientos.Include(m => m.Lineas).SingleAsync(m => m.Tipo == TipoMovimiento.AjustePositivo))
            .Lineas.Single().CostoUnitarioMxn.Should().Be(150m);
    }

    [Fact]
    public async Task Devolucion_de_venta_sin_la_salida_en_el_ERP_entra_al_promedio()
    {
        var (db, unica, sub) = Db();
        db.SaldosInventario.Add(new SaldoInventario(unica, sub, Articulo, 100m, 100m, Variante, "L-1"));
        await db.SaveChangesAsync();

        await EntradaAppAlmacenes.RegistrarAsync(db, Puertos(), EmpresaId, Usuario, Sucursal, "m", null, "msg-d",
            [Hojas(1, "L-1") with { CostoRespaldoMxn = 130m }], default, TipoMovimiento.AjustePositivo,
            new OrigenApp("DEVOLUCION_VENTA", "MV-0012345")); // la venta por albarán no pasa por la app
        await db.SaveChangesAsync();

        (await db.LineasMovimiento.SingleAsync()).CostoUnitarioMxn.Should().Be(100m);
    }

    [Fact]
    public async Task Cancelacion_de_entrada_sale_al_costo_con_que_entro()
    {
        var (db, unica, sub) = Db();
        // La entrada EN-5 entró a 150 (sin promedio previo: al costo del mensaje); el saldo de hoy está a 100.
        var (_, folioEntrada, _) = await EntradaAppAlmacenes.RegistrarAsync(db, Puertos(), EmpresaId, Usuario, Sucursal, "m", null, "msg-e",
            [Hojas(3, "L-9") with { CostoRespaldoMxn = 150m }], default, TipoMovimiento.AjustePositivo);
        await AplicadoAsync(db, "ENTRADA_ALMACEN", "EN-5", folioEntrada);
        db.SaldosInventario.Add(new SaldoInventario(unica, sub, Articulo, 3 * 20.1422m, 100m, Variante, "L-9"));
        await db.SaveChangesAsync();

        var (estado, folio, _) = await SalidaAppAlmacenes.RegistrarAsync(db, new Captura(), EmpresaId, Usuario,
            [Prestamo(3 * 20.1422m, null, "L-9")], default, new OrigenApp("CANCELACION_ENTRADA", "EN-5"));
        await db.SaveChangesAsync();

        estado.Should().Be(SalidaAppAlmacenes.Recibido);
        (await db.Movimientos.Include(m => m.Lineas).SingleAsync(m => m.Folio == folio)).Lineas.Single().CostoUnitarioMxn.Should().Be(150m);
    }

    [Fact]
    public void El_costo_de_origen_prefiere_el_mismo_lote_y_pondera_por_cantidad()
    {
        List<CostoOrigenApp.LineaOrigen> lineas =
        [
            new(Articulo, Variante, "L-1", 2m, 150m),
            new(Articulo, Variante, "L-2", 1m, 120m),
            new(Articulo, Variante, "L-2", 3m, 100m),
            new(Guid.NewGuid(), Variante, "L-1", 9m, 999m),
        ];
        CostoOrigenApp.Costo(lineas, Articulo, Variante, "L-1").Should().Be(150m);
        CostoOrigenApp.Costo(lineas, Articulo, Variante, " L-2 ").Should().Be(105m);
        CostoOrigenApp.Costo(lineas, Articulo, Variante, "L-X").Should().Be(120m); // sin ese lote: toda la medida, (300 + 120 + 300) / 6
        CostoOrigenApp.Costo(lineas, Articulo, null, "L-1").Should().BeNull();
    }

    // ── motivoClave (M-283, v9): dato del movimiento, sin catálogo ─────────────────────────────

    [Fact]
    public async Task La_entrada_y_la_salida_manual_guardan_su_motivoClave_en_el_movimiento()
    {
        var (db, unica, sub) = Db();
        var (_, folioEntrada, _) = await EntradaAppAlmacenes.RegistrarAsync(db, Puertos(), EmpresaId, Usuario, Sucursal, "m", null, "msg-k",
            [Hojas(1) with { CostoRespaldoMxn = 130m }], default, TipoMovimiento.AjustePositivo, motivoClave: " ZZ_ARNES_DOC_SOBRANTE ");
        db.SaldosInventario.Add(new SaldoInventario(unica, sub, Articulo, 100m, 100m, Variante, "L-1"));
        await db.SaveChangesAsync();
        var (_, folioSalida, _) = await SalidaAppAlmacenes.RegistrarAsync(db, new Captura(), EmpresaId, Usuario,
            [Prestamo(1m, null) with { MotivoClave = "PRESTAMO" }], default);
        await db.SaveChangesAsync();

        (await db.Movimientos.SingleAsync(m => m.Folio == folioEntrada)).MotivoClave.Should().Be("ZZ_ARNES_DOC_SOBRANTE");
        (await db.Movimientos.SingleAsync(m => m.Folio == folioSalida)).MotivoClave.Should().Be("PRESTAMO");
    }

    private static EntradaAppPuertos Puertos(OcLectura? oc = null, Captura? eventos = null, bool periodoAbierto = true) =>
        new(new FakeOcs(oc), new FactorUno(), new FakePeriodo(periodoAbierto), eventos ?? new Captura());

    private sealed class FakeOcs(OcLectura? oc) : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct) => Task.FromResult(oc?.Id == ocId ? oc : null);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
        public Task<Guid?> ObtenerIdPorFolioAsync(string folio, CancellationToken ct) => Task.FromResult(oc?.Folio == folio ? oc?.Id : null);
    }

    private sealed class FactorUno : IConversionUnidadPort
    {
        public Task<ConversionUnidad> ConvertirAsync(Guid articuloId, decimal cantidad, string? unidadCapturada, string unidadDocumento, CancellationToken ct) =>
            Task.FromResult(new ConversionUnidad(cantidad, unidadDocumento, cantidad, 1m, unidadDocumento, cantidad));
    }

    private sealed class FakePeriodo(bool abierto) : IPeriodoContableReadPort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(abierto);
    }

    private sealed class Captura : IIntegrationEventPublisher
    {
        public List<object> Publicados { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken ct) { Publicados.Add(integrationEvent); return Task.CompletedTask; }
    }

    private sealed class FakeEmpresa(Guid current) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = current;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }
}
