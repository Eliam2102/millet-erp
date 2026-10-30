using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Application.Cajas.Cobros;
using Millet.Facturacion.Application.Reportes.CorteCaja;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cajas;

/// <summary>
/// Corte de caja de una sesión (FIX-12; Manual de Sucursales §2.4, R18,
/// CA6.10): cuadra con el arqueo (mismas filas que caja_movimiento), trae el
/// nombre del cajero, los vouchers de tarjeta, los comprobantes cobrados con
/// sus NC una sola vez por cobro, y solo lo ven el cajero o quien supervisa.
/// </summary>
public sealed class CorteCajaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cajero = Guid.NewGuid();
    private static readonly Guid SucursalId = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 7, 10, 18, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(Empresa));

    private static DatosFiscalesReceptor Receptor() =>
        new("AAA010101AAA", "Cliente Mostrador", "601", "97000", "G03", "MEX", false);

    private static DatosFiscalesEmisor Emisor() => new("BBB010101BBB", "Millet", "601", "76120");

    private static FacturaVenta FacturaTimbrada(
        decimal valor, string folio, Guid? sucursal = null, string metodoPago = "PUE", string moneda = "MXN",
        DateTimeOffset? fechaTimbrado = null)
    {
        var fv = FacturaVenta.CrearBorrador(Empresa, folio, 1, sucursal ?? SucursalId, null, null, Receptor(), Emisor(),
            metodoPago, metodoPago == "PPD" ? "99" : "01", moneda, moneda == "MXN" ? null : 17.5m, 2026, 7, 1,
            ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, valor, 0m, "02", 0m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, fechaTimbrado ?? Ahora, null, null);
        return fv;
    }

    private static NotaCredito NcTimbrada(
        MotivoNotaCredito motivo, Guid facturaId, decimal monto, string folio, DateTimeOffset? fechaTimbrado = null)
    {
        var nc = motivo == MotivoNotaCredito.Ranura
            ? NotaCredito.CrearRanura(Empresa, folio, 1, SucursalId, null, null, Receptor(), Emisor(),
                "01", "MXN", null, 2026, 7, 1, facturaId, monto, null, "Descuento Ranura")
            : NotaCredito.CrearBonificacion(Empresa, folio, 1, SucursalId, null, null, Receptor(), Emisor(),
                "01", "MXN", null, 2026, 7, 1, facturaId, monto, null, null);
        nc.MarcarTimbradoEnProceso();
        nc.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, fechaTimbrado ?? Ahora, null, null);
        return nc;
    }

    private static async Task<CajaSesion> SembrarSesionAsync(FacturacionDbContext db)
    {
        var caja = Caja.Crear(Empresa, "Caja Mostrador", null);
        var sesion = CajaSesion.Abrir(
            Empresa, caja.Id, SucursalId, Cajero, 500m,
            new DateOnly(2026, 7, 10), Ahora.AddHours(-2), null);
        db.Cajas.Add(caja);
        db.CajaSesiones.Add(sesion);
        db.CajaMovimientos.Add(CajaMovimiento.Crear(
            Empresa, sesion.Id, TipoCajaMovimiento.FondoApertura, CajaSesion.FormaPagoEfectivo,
            500m, Cajero, "Fondo de apertura"));
        await db.SaveChangesAsync();
        return sesion;
    }

    private static Task<CobroMostradorResponse> CobrarAsync(
        FacturacionDbContext db, Guid comprobanteId, params CobroFormaPagoInput[] formas) =>
        new RegistrarCobroMostradorHandler(
                db, new FakeEmpresaContext(Empresa), new FakeUserContext(Cajero), new FakeClock(Ahora),
                new FakeSucursalesReadPort(), new FakeAlcanceCajaEvaluator(),
                new FakeIntegrationEventPublisher(), new FakeCatalogosSatReadPort())
            .Handle(new RegistrarCobroMostradorCommand(comprobanteId, formas), CancellationToken.None);

    private static CorteCajaSesionHandler Corte(
        FacturacionDbContext db, Guid? usuario = null, DateTimeOffset? clock = null, params string[] permisos) =>
        new(db, new FakeUserContext(usuario ?? Cajero), new FakeCurrentUserPermissions(permisos),
            new FakeUsuariosReadPort(new Dictionary<Guid, string> { [Cajero] = "Ana Cajera" }),
            new FakeSucursalesReadPort(), new FakeClock(clock ?? Ahora));

    [Fact]
    public async Task Demo19Oct_52_Cobros7510_MasFondo500_Corte8010()
    {
        using var db = NewDb();
        var sesion = await SembrarSesionAsync(db);
        var f1 = FacturaTimbrada(5000m, "FA-1");
        var f2 = FacturaTimbrada(2510m, "FA-2");
        db.FacturasVenta.AddRange(f1, f2);
        await db.SaveChangesAsync();
        await CobrarAsync(db, f1.Id, new("01", 2000m, null), new("04", 3000m, "VOUCHER", TarjetaUltimos4: "3000"));
        await CobrarAsync(db, f2.Id, new CobroFormaPagoInput("03", 2510m, "TRANSFERENCIA"));
        db.ChangeTracker.Clear();
        var corte = await Corte(db).Handle(new(sesion.Id), default);
        corte.Reporte.Totales!["importe"].Should().Be(8010m);
        corte.PorFormaPago.Single(f => f.FormaPago == "01").MontoSistema.Should().Be(2500m);
        corte.PorFormaPago.Single(f => f.FormaPago == "04").MontoSistema.Should().Be(3000m);
        corte.PorFormaPago.Single(f => f.FormaPago == "03").MontoSistema.Should().Be(2510m);
        var viva = await new Millet.Facturacion.Application.Cajas.Sesiones.CajaSesionDetalleHandler(
            db, new FakeCurrentUserPermissions("facturacion.caja.supervisar"))
            .Handle(new(sesion.Id), default);
        viva.TotalesPorForma.Sum(f => f.MontoSistema).Should().Be(8010m);
        viva.TotalesPorForma.Sum(f => f.MontoSistema).Should().Be(7510m + viva.FondoApertura);
    }

    [Fact]
    public async Task Corte_cuadra_con_el_arqueo_y_trae_cajero_vouchers_y_nc_una_vez()
    {
        using var db = NewDb();
        var sesion = await SembrarSesionAsync(db);

        // Venta con ranura: factura por 1,160 con NC de 116 → la caja cobra 1,044 (Matriz I2).
        var factura = FacturaTimbrada(1000m, "F-1");
        db.FacturasVenta.Add(factura);
        var nc = NotaCredito.CrearBonificacion(Empresa, "NC-1", 1, SucursalId, null, null, Receptor(), Emisor(),
            "01", "MXN", null, 2026, 7, 1, factura.Id, 116m, null, null);
        nc.MarcarTimbradoEnProceso();
        nc.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        db.NotasCredito.Add(nc);

        // Anticipo (apartado) cobrado con tarjeta de débito.
        var anticipo = FacturaAnticipo.CrearBorrador(Empresa, "A-1", 1, SucursalId, null, null,
            Receptor(), Emisor(), "01", "MXN", null, 2026, 7, TipoAnticipo.ClientesMxp,
            null, Guid.NewGuid(), 500m, 0.16m, canalVentaId: 1);
        anticipo.MarcarTimbradoEnProceso();
        anticipo.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        db.FacturasAnticipo.Add(anticipo);

        // Complemento de pago de mostrador (Total del CFDI = 0; el importe vive en el REPP).
        var repp = ReciboPago.CrearBorrador(Empresa, "P-1", 1, SucursalId, null, null, Receptor(), Emisor(),
            2026, 7, Ahora, "MXN", 1);
        repp.EstablecerImporteTotalPago(750m);
        repp.MarcarTimbradoEnProceso();
        repp.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        db.RecibosPago.Add(repp);
        await db.SaveChangesAsync();

        var neto = factura.Total - 116m;
        await CobrarAsync(db, factura.Id,
            new CobroFormaPagoInput("01", neto - 400m), new CobroFormaPagoInput("04", 400m, "AUT-77", TarjetaUltimos4: "1234"));
        await CobrarAsync(db, anticipo.Id, new CobroFormaPagoInput("28", anticipo.Total, "AUT-88", TarjetaUltimos4: "5678"));
        await CobrarAsync(db, repp.Id, new CobroFormaPagoInput("01", 750m));
        db.ChangeTracker.Clear();

        var r = await Corte(db).Handle(new CorteCajaSesionQuery(sesion.Id), CancellationToken.None);

        r.Encabezado.Cajero.Should().Be("Ana Cajera");
        r.Encabezado.Sucursal.Should().Be("MID — Mérida");
        r.Encabezado.Caja.Should().Be("Caja Mostrador");
        r.Reporte.FiltrosAplicados["Cajero"].Should().Be("Ana Cajera");

        // Una fila por movimiento: fondo + 2 formas de la factura + anticipo + REPP.
        r.Reporte.Filas.Should().HaveCount(5);
        // El total del corte es la suma de los movimientos = esperado del arqueo.
        r.Reporte.Totales!["importe"].Should().Be(500m + neto + anticipo.Total + 750m);

        // Por forma de pago (lo que congela el arqueo).
        var porForma = r.PorFormaPago.ToDictionary(f => f.FormaPago);
        porForma["01"].MontoSistema.Should().Be(500m + neto - 400m + 750m);
        porForma["01"].Cobros.Should().Be(2);
        porForma["04"].MontoSistema.Should().Be(400m);
        porForma["28"].MontoSistema.Should().Be(anticipo.Total);

        // Vouchers de la terminal visibles en el corte: autorización y últimos 4 (86e3tfdvt).
        r.Reporte.Filas.Where(f => f.Referencia != null).Select(f => f.Referencia)
            .Should().BeEquivalentTo(["AUT-77 · **** 1234", "AUT-88 · **** 5678"]);

        // Total del CFDI y NC: una sola vez por cobro (informativos, no son dinero).
        var filasFactura = r.Reporte.Filas.Where(f => f.Comprobante == "Factura F-1").ToList();
        filasFactura.Should().HaveCount(2);
        filasFactura.Count(f => f.TotalComprobante != null).Should().Be(1);
        filasFactura.Single(f => f.TotalComprobante != null).NotasCredito.Should().Be(116m);

        r.Reporte.Filas.Should().Contain(f => f.Comprobante == "Anticipo A-1" && f.TotalComprobante == anticipo.Total);
        r.Reporte.Filas.Should().Contain(f => f.Comprobante == "Complemento de pago P-1" && f.TotalComprobante == 750m);
        r.Reporte.Filas.Should().Contain(f => f.Tipo == "Fondo de apertura" && f.Importe == 500m);
    }

    /// <summary>
    /// Demo del 12-oct (M19): la cajera entrega qué se facturó, las NC, las
    /// cobranzas, los apartados y la TPV. Un día con venta con Ranura cobrada,
    /// venta a crédito sin cobrar, bonificación a una factura de otro día,
    /// anticipo cobrado con tarjeta, una factura cancelada, una en dólares y
    /// documentos fuera de la sesión (otra sucursal, antes de abrir).
    /// </summary>
    [Fact]
    public async Task Corte_trae_facturado_notas_de_credito_cobranzas_apartados_y_tpv_del_punto_de_venta()
    {
        using var db = NewDb();
        var sesion = await SembrarSesionAsync(db);

        var ranura = FacturaTimbrada(1000m, "F-101");                       // con Ranura de 116
        var credito = FacturaTimbrada(2000m, "F-103", metodoPago: "PPD");   // a crédito, no se cobra
        var vieja = FacturaTimbrada(500m, "F-090", fechaTimbrado: Ahora.AddDays(-7));
        var cancelada = FacturaTimbrada(100m, "F-104");
        cancelada.MarcarCancelacionPendiente();
        cancelada.MarcarCancelado();
        var dolares = FacturaTimbrada(100m, "F-105", moneda: "USD");
        var otraSucursal = FacturaTimbrada(300m, "F-OTRA", sucursal: Guid.NewGuid());
        var antesDeAbrir = FacturaTimbrada(400m, "F-ANTES", fechaTimbrado: Ahora.AddHours(-3));
        db.FacturasVenta.AddRange(ranura, credito, vieja, cancelada, dolares, otraSucursal, antesDeAbrir);

        var ncRanura = NcTimbrada(MotivoNotaCredito.Ranura, ranura.Id, 116m, "NC-R1");
        var ncBonificacion = NcTimbrada(MotivoNotaCredito.Bonificacion, vieja.Id, 58m, "NC-B1");
        var ncVieja = NcTimbrada(MotivoNotaCredito.Bonificacion, vieja.Id, 10m, "NC-VIEJA", Ahora.AddDays(-6));
        db.NotasCredito.AddRange(ncRanura, ncBonificacion, ncVieja);

        var anticipo = FacturaAnticipo.CrearBorrador(Empresa, "A-01", 1, SucursalId, null, null,
            Receptor(), Emisor(), "01", "MXN", null, 2026, 7, TipoAnticipo.ClientesMxp,
            null, Guid.NewGuid(), 300m, 0.16m, canalVentaId: 1);
        anticipo.MarcarTimbradoEnProceso();
        anticipo.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        db.FacturasAnticipo.Add(anticipo);
        await db.SaveChangesAsync();

        await CobrarAsync(db, ranura.Id, new CobroFormaPagoInput("01", ranura.Total - 116m));
        await CobrarAsync(db, anticipo.Id, new CobroFormaPagoInput("04", anticipo.Total, "482913", TarjetaUltimos4: "0417"));
        db.ChangeTracker.Clear();

        var r = await Corte(db).Handle(new CorteCajaSesionQuery(sesion.Id), CancellationToken.None);

        // Facturado: las del punto de venta y de la sesión, con su estado y su cobro.
        r.Facturado.Filas.Select(f => f.Comprobante).Should().BeEquivalentTo(
            ["Factura F-101", "Factura F-103", "Factura F-104", "Factura F-105", "Anticipo A-01"]);
        r.Facturado.Filas.Single(f => f.Comprobante == "Factura F-101").Cobro.Should().Be("Cobrada en esta sesión");
        r.Facturado.Filas.Single(f => f.Comprobante == "Factura F-103").Cobro.Should().Be("Crédito (PPD)");
        r.Facturado.Filas.Single(f => f.Comprobante == "Factura F-104").EstadoCfdi.Should().Be("Cancelado");
        r.Facturado.Filas.Single(f => f.Comprobante == "Factura F-105").Moneda.Should().Be("USD");
        // Totales: solo vigentes en pesos (sin la cancelada ni la de dólares).
        r.Facturado.Totales!["total"].Should().Be(ranura.Total + credito.Total + anticipo.Total);

        // NC: la de Ranura y la bonificación de una factura de otro día; no la emitida antes.
        r.NotasCredito.Filas.Select(n => n.Folio).Should().BeEquivalentTo(["NC-R1", "NC-B1"]);
        r.NotasCredito.Filas.Single(n => n.Folio == "NC-R1").Motivo.Should().Be("Ranura");
        r.NotasCredito.Filas.Single(n => n.Folio == "NC-B1").FacturaRelacionada.Should().Be("F-090");
        r.NotasCredito.Totales!["total"].Should().Be(116m + 58m);

        r.Resumen.Facturado.Should().Be(ranura.Total + credito.Total + anticipo.Total);
        r.Resumen.Facturas.Should().Be(3);
        r.Resumen.NotasCredito.Should().Be(174m);
        r.Resumen.FacturadoNeto.Should().Be(ranura.Total + credito.Total + anticipo.Total - 174m);
        r.Resumen.Cobranzas.Should().Be(ranura.Total - 116m);
        r.Resumen.Apartados.Should().Be(anticipo.Total);
        r.Resumen.Tpv.Should().Be(anticipo.Total);
        r.Resumen.TotalIngresado.Should().Be(ranura.Total - 116m + anticipo.Total);

        // Las secciones llevan el encabezado del corte para el PDF.
        r.Facturado.Titulo.Should().StartWith("Facturado · Caja Mostrador");
        r.NotasCredito.FiltrosAplicados["Cajero"].Should().Be("Ana Cajera");
        r.Reporte.FiltrosAplicados["Notas de crédito"].Should().Contain("(2)");
    }

    [Fact]
    public async Task Corte_de_sesion_extemporanea_no_toma_lo_facturado_el_dia_siguiente()
    {
        using var db = NewDb();
        var sesion = await SembrarSesionAsync(db);   // día de operación 10-jul, abierta y sin cerrar
        db.FacturasVenta.AddRange(
            FacturaTimbrada(100m, "F-DEL-DIA"),
            FacturaTimbrada(100m, "F-DIA-SIGUIENTE", fechaTimbrado: Ahora.AddDays(1)));
        await db.SaveChangesAsync();

        var r = await Corte(db, clock: Ahora.AddDays(1).AddHours(2))
            .Handle(new CorteCajaSesionQuery(sesion.Id), CancellationToken.None);

        r.Facturado.Filas.Select(f => f.Comprobante).Should().BeEquivalentTo(["Factura F-DEL-DIA"]);
    }

    [Fact]
    public void Fin_del_dia_de_operacion_es_la_medianoche_local_en_utc()
    {
        // Npgsql solo acepta DateTimeOffset con offset 0 en timestamptz.
        var fin = Millet.Facturacion.Application.Cajas.Sesiones.DiaOperacion.FinDelDia(
            new DateOnly(2026, 10, 13), "America/Merida");

        fin.Offset.Should().Be(TimeSpan.Zero);
        fin.Should().Be(new DateTimeOffset(2026, 10, 14, 6, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Corte_incluye_lo_que_emitio_el_cajero_aunque_no_tenga_canal()
    {
        using var db = NewDb();
        var sesion = await SembrarSesionAsync(db);

        // Factura manual del cajero sin canal: fuera del alcance por canal,
        // pero la emitió él durante la sesión.
        var manual = FacturaVenta.CrearBorrador(Empresa, "F-MAN", 1, SucursalId, null, Cajero, Receptor(), Emisor(),
            "PUE", "01", "MXN", null, 2026, 7, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        manual.AgregarLinea(null, "01010101", "P", "H87", 1m, 100m, 0m, "02", 0m, null, null);
        manual.RecalcularTotales();
        manual.MarcarTimbradoEnProceso();
        manual.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        db.FacturasVenta.Add(manual);
        await db.SaveChangesAsync();

        var r = await Corte(db).Handle(new CorteCajaSesionQuery(sesion.Id), CancellationToken.None);

        r.Facturado.Filas.Should().ContainSingle(f => f.Comprobante == "Factura F-MAN" && f.Cobro == "Por cobrar");
    }

    [Fact]
    public async Task Corte_muestra_la_reversa_y_marca_el_cobro_cancelado()
    {
        using var db = NewDb();
        var sesion = await SembrarSesionAsync(db);
        var factura = FacturaTimbrada(200m, "F-2");
        db.FacturasVenta.Add(factura);
        await db.SaveChangesAsync();

        var cobro = await CobrarAsync(db, factura.Id, new CobroFormaPagoInput("01", factura.Total));
        await new CancelarCobroMostradorHandler(
                db, new FakeEmpresaContext(Empresa), new FakeUserContext(Cajero), new FakeClock(Ahora),
                new FakeSucursalesReadPort(), new FakeIntegrationEventPublisher())
            .Handle(new CancelarCobroMostradorCommand(cobro.Id, "Error de captura"), CancellationToken.None);
        db.ChangeTracker.Clear();

        var r = await Corte(db).Handle(new CorteCajaSesionQuery(sesion.Id), CancellationToken.None);

        r.Reporte.Filas.Should().Contain(f => f.Tipo == "Reversa de cobro" && f.Importe == -factura.Total);
        r.Reporte.Filas.Where(f => f.Comprobante == "Factura F-2")
            .Should().OnlyContain(f => f.EstadoCobro == "Cancelado");
        // Cobro y reversa se anulan: queda solo el fondo.
        r.Reporte.Totales!["importe"].Should().Be(500m);
    }

    [Fact]
    public async Task Corte_de_otra_persona_sin_permiso_de_supervision_falla()
    {
        using var db = NewDb();
        var sesion = await SembrarSesionAsync(db);

        var act = () => Corte(db, usuario: Guid.NewGuid())
            .Handle(new CorteCajaSesionQuery(sesion.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>().Where(e => e.Code == "CORTE_PERMISO_INSUFICIENTE");
    }

    [Theory]
    [InlineData("facturacion.caja.supervisar")]
    [InlineData("facturacion.caja.liquidar")]
    public async Task Corte_de_otra_persona_con_supervision_o_liquidacion_se_consulta(string permiso)
    {
        using var db = NewDb();
        var sesion = await SembrarSesionAsync(db);

        var r = await Corte(db, Guid.NewGuid(), null, permiso)
            .Handle(new CorteCajaSesionQuery(sesion.Id), CancellationToken.None);

        r.Encabezado.SesionId.Should().Be(sesion.Id);
    }

    [Fact]
    public async Task Corte_de_sesion_inexistente_responde_no_encontrada()
    {
        using var db = NewDb();

        var act = () => Corte(db).Handle(new CorteCajaSesionQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>();
    }
}
