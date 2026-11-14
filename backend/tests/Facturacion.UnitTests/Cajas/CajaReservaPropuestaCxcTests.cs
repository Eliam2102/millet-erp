using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.Facturacion.Application.Cajas.Cobros;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Infrastructure.CuentasPorCobrar;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cajas;

/// <summary>
/// H-U41 (doble cobro): una factura PUE con propuesta de aplicación de CxC abierta (en revisión, o confirmada por
/// Tesorería con el REP pendiente) ya no es cobrable en caja por el importe propuesto. Al rechazarse la propuesta,
/// vuelve a ser cobrable. Datos ficticios.
/// </summary>
public sealed class CajaReservaPropuestaCxcTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cajero = Guid.NewGuid();
    private static readonly Guid SucursalId = Guid.NewGuid();
    private static readonly Guid Proponente = Guid.NewGuid();
    private static readonly Guid Tesoreria = Guid.NewGuid();
    private static readonly Guid Cliente = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(Empresa));

    private static CuentasPorCobrarDbContext NewCxc() =>
        new(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(Empresa));

    private static FacturaVenta FacturaPue(decimal valor, string folio)
    {
        var fv = FacturaVenta.CrearBorrador(Empresa, folio, 1, SucursalId, null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente Prueba", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Emisor", "601", "76120"),
            "PUE", "03", "MXN", null, 2026, 11, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, valor, 0m, "02", 0m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        return fv;
    }

    private static PropuestaAplicacionPago Propuesta(Guid facturaCarteraId, string uuid, decimal importe) =>
        PropuestaAplicacionPago.Crear(Empresa, Cliente, "DEP-1", importe, "MXN", "REM-1",
            [(uuid, facturaCarteraId, importe, null)], 0m, propuestoPor: Proponente, ahora: Ahora);

    private static async Task<(FacturacionDbContext Db, CuentasPorCobrarDbContext Cxc, FacturaVenta Factura, PropuestaAplicacionPago Propuesta)>
        EscenarioAsync()
    {
        var db = NewDb();
        var caja = Caja.Crear(Empresa, "Caja Mostrador", null);
        db.Cajas.Add(caja);
        db.CajaSesiones.Add(CajaSesion.Abrir(Empresa, caja.Id, SucursalId, Cajero, 500m,
            new DateOnly(2026, 11, 12), Ahora.AddHours(-2), null));
        var factura = FacturaPue(1100m, "FX-000001");
        db.FacturasVenta.Add(factura);
        await db.SaveChangesAsync();

        var cxc = NewCxc();
        var cartera = FacturaCartera.Crear(Empresa, factura.Id, Cliente, "AAA010101AAA", "Cliente Prueba",
            factura.Uuid!, factura.Folio, factura.Total, "MXN", "PUE", Ahora, Ahora.AddDays(30));
        cxc.FacturasCartera.Add(cartera);
        var propuesta = Propuesta(cartera.Id, factura.Uuid!, factura.Total);
        cxc.PropuestasAplicacionPago.Add(propuesta);
        await cxc.SaveChangesAsync();
        return (db, cxc, factura, propuesta);
    }

    private static RegistrarCobroMostradorHandler Registrar(FacturacionDbContext db, CuentasPorCobrarDbContext cxc) =>
        new(db, new FakeEmpresaContext(Empresa), new FakeUserContext(Cajero), new FakeClock(Ahora),
            new FakeSucursalesReadPort(), new FakeAlcanceCajaEvaluator(), new FakeIntegrationEventPublisher(),
            new FakeCatalogosSatReadPort(), reservasCxc: new ReservaCobroCxcReadAdapter(cxc));

    private static Task<IReadOnlyList<ComprobanteCobrableItem>> Cobrables(FacturacionDbContext db, CuentasPorCobrarDbContext cxc) =>
        new ListarComprobantesCobrablesHandler(db, new FakeAlcanceCajaEvaluator(), new ReservaCobroCxcReadAdapter(cxc))
            .Handle(new ListarComprobantesCobrablesQuery(), CancellationToken.None);

    [Fact]
    public async Task Propuesta_confirmada_con_REP_pendiente_saca_la_factura_de_caja_y_no_deja_cobrarla()
    {
        var (db, cxc, factura, propuesta) = await EscenarioAsync();
        propuesta.Confirmar(Tesoreria, Ahora, Guid.NewGuid());
        await cxc.SaveChangesAsync();

        (await Cobrables(db, cxc)).Should().NotContain(i => i.ComprobanteId == factura.Id);

        var act = () => Registrar(db, cxc).Handle(new RegistrarCobroMostradorCommand(
            factura.Id, [new CobroFormaPagoInput("01", factura.Total)]), CancellationToken.None);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "COBRO_RESERVADO_POR_PROPUESTA");
        db.CobrosMostrador.Should().BeEmpty();
    }

    [Fact]
    public async Task Propuesta_en_revision_tambien_reserva()
    {
        var (db, cxc, factura, _) = await EscenarioAsync();

        (await Cobrables(db, cxc)).Should().NotContain(i => i.ComprobanteId == factura.Id);
    }

    [Fact]
    public async Task Propuesta_rechazada_libera_la_factura_para_caja()
    {
        var (db, cxc, factura, propuesta) = await EscenarioAsync();
        propuesta.Rechazar(Tesoreria, "El depósito no corresponde", Ahora);
        await cxc.SaveChangesAsync();

        var item = (await Cobrables(db, cxc)).Should().ContainSingle(i => i.ComprobanteId == factura.Id).Subject;
        item.Total.Should().Be(factura.Total);
        item.MontoReservadoCxc.Should().Be(0m);

        var cobro = await Registrar(db, cxc).Handle(new RegistrarCobroMostradorCommand(
            factura.Id, [new CobroFormaPagoInput("01", factura.Total)]), CancellationToken.None);
        cobro.Total.Should().Be(factura.Total);
    }

    [Fact]
    public async Task PPD_con_REP_timbrado_libera_la_reserva_y_PUE_confirmada_la_conserva()
    {
        using var cxc = NewCxc();
        var ppdId = Guid.NewGuid();
        var pueId = Guid.NewGuid();
        var ppd = FacturaCartera.Crear(Empresa, ppdId, Cliente, "AAA010101AAA", "Cliente Prueba",
            "UUID-PPD", "FX-PPD", 500m, "MXN", "PPD", Ahora, Ahora.AddDays(30));
        var pue = FacturaCartera.Crear(Empresa, pueId, Cliente, "AAA010101AAA", "Cliente Prueba",
            "UUID-PUE", "FX-PUE", 300m, "MXN", "PUE", Ahora, Ahora.AddDays(30));
        cxc.FacturasCartera.AddRange(ppd, pue);
        var conRep = Propuesta(ppd.Id, "UUID-PPD", 500m);
        var sinRep = Propuesta(pue.Id, "UUID-PUE", 300m);
        cxc.PropuestasAplicacionPago.AddRange(conRep, sinRep);
        conRep.Confirmar(Tesoreria, Ahora, Guid.NewGuid());
        conRep.MarcarReppTimbrado(Guid.NewGuid());
        sinRep.Confirmar(Tesoreria, Ahora, Guid.NewGuid());
        await cxc.SaveChangesAsync();

        var reservado = await new ReservaCobroCxcReadAdapter(cxc).ReservadoPorFacturaAsync([ppdId, pueId], CancellationToken.None);

        reservado.Should().NotContainKey(ppdId);
        reservado[pueId].Should().Be(300m);
    }
}
