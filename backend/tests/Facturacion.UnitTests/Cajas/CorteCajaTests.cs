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

    private static FacturaVenta FacturaTimbrada(decimal valor, string folio)
    {
        var fv = FacturaVenta.CrearBorrador(Empresa, folio, 1, SucursalId, null, null, Receptor(), Emisor(),
            "PUE", "01", "MXN", null, 2026, 7, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, valor, 0m, "02", 0m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        return fv;
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
        FacturacionDbContext db, Guid? usuario = null, params string[] permisos) =>
        new(db, new FakeUserContext(usuario ?? Cajero), new FakeCurrentUserPermissions(permisos),
            new FakeUsuariosReadPort(new Dictionary<Guid, string> { [Cajero] = "Ana Cajera" }),
            new FakeSucursalesReadPort(), new FakeClock(Ahora));

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
            new CobroFormaPagoInput("01", neto - 400m), new CobroFormaPagoInput("04", 400m, "AUT-77"));
        await CobrarAsync(db, anticipo.Id, new CobroFormaPagoInput("28", anticipo.Total, "AUT-88"));
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

        // Vouchers de la terminal visibles en el corte.
        r.Reporte.Filas.Where(f => f.Referencia != null).Select(f => f.Referencia)
            .Should().BeEquivalentTo(["AUT-77", "AUT-88"]);

        // Total del CFDI y NC: una sola vez por cobro (informativos, no son dinero).
        var filasFactura = r.Reporte.Filas.Where(f => f.Comprobante == "Factura F-1").ToList();
        filasFactura.Should().HaveCount(2);
        filasFactura.Count(f => f.TotalComprobante != null).Should().Be(1);
        filasFactura.Single(f => f.TotalComprobante != null).NotasCredito.Should().Be(116m);

        r.Reporte.Filas.Should().Contain(f => f.Comprobante == "Anticipo A-1" && f.TotalComprobante == anticipo.Total);
        r.Reporte.Filas.Should().Contain(f => f.Comprobante == "Complemento de pago P-1" && f.TotalComprobante == 750m);
        r.Reporte.Filas.Should().Contain(f => f.Tipo == "Fondo de apertura" && f.Importe == 500m);
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

        var r = await Corte(db, Guid.NewGuid(), permiso)
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
