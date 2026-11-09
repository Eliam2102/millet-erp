using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Anticipos.AplicarAnticipoAFactura;
using Millet.Facturacion.Application.Anticipos.EmitirFacturaAnticipo;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.NotasCredito.EmitirNotaCreditoBonificacion;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Anticipos;

/// <summary>
/// F1-CXC-05 Parte 2: compensación del anticipo contra una factura ya timbrada del cliente (NC de amortización 07) y
/// NC de bonificación sin saldos negativos.
/// </summary>
public sealed class AplicarAnticipoAFacturaTests
{
    private const string Rfc = "AAA010101AAA";
    private static readonly DateTimeOffset Ahora = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _cliente = Guid.NewGuid();
    private readonly FakeIntegrationEventPublisher _eventos = new();

    private FacturacionDbContext Db() => new(new DbContextOptionsBuilder<FacturacionDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new FakeEmpresaContext(_empresa));

    private async Task<Anticipo> AnticipoAsync(FacturacionDbContext db, string moneda = "MXN")
    {
        await new EmitirFacturaAnticipoHandler(db, new FakeSender(new ReservarFolioResponse("FANT-000001", 1, "")),
            new FakePeriodoContablePort(), new FakeCatalogosSatReadPort(), new FakeFiscalApiClient(), new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(_empresa, "MIL010101AAA", "Millet", "601", 0.16m, "76120")),
            new FakeIntegrationEventPublisher(), new FakeEmpresaContext(_empresa), new FakeUserContext(Guid.NewGuid()),
            new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(db))
            .Handle(new EmitirFacturaAnticipoCommand(Guid.NewGuid(), _cliente, Rfc, "Cliente Maquila", "601", "97000", "G03",
                "MEX", "BBB010101BBB", "601", "PUE", "03", moneda, moneda == "MXN" ? null : 18m,
                moneda == "MXN" ? TipoAnticipo.ClientesMxp : TipoAnticipo.ClientesUsd, 1000m, 0.16m, null, null, "AW-1", null, null),
                default);
        return await db.Anticipos.Include(a => a.Vinculaciones).SingleAsync();
    }

    private async Task<FacturaVenta> FacturaAsync(FacturacionDbContext db, string rfc = Rfc, string moneda = "MXN", decimal precio = 2000m)
    {
        var fv = FacturaVenta.CrearBorrador(_empresa, "F-000010", 10, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor(rfc, "Cliente Maquila", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("MIL010101AAA", "Millet", "601", "76120"), "PPD", "99", moneda, moneda == "MXN" ? null : 18m,
            2026, 10, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "Vidrio", "H87", 1m, precio, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado("99999999-9999-9999-9999-999999999999", null, null, null, Ahora, null, null);
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();
        return fv;
    }

    private AplicarAnticipoAFacturaHandler Handler(FacturacionDbContext db, decimal? saldoCxc = null) =>
        new(db, new FakeSender(new ReservarFolioResponse("NC-000020", 20, "")), new FakePeriodoContablePort(),
            new FakeSaldoFacturaReadPort(saldoCxc), new FakeFiscalApiClient(), new FakeCfdiRepositorioPort(), _eventos,
            new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora), new FakeCatalogosSatReadPort(tipoCambio: 18.7592m));

    // ---- 86e3zg0vb: anticipo de exportación (XEXX, USD, IVA 0 %) aplicado días después de la factura final ----

    private const string Xexx = "XEXX010101000";

    private async Task<Anticipo> AnticipoExtranjeroAsync(FacturacionDbContext db)
    {
        var maestro = new ClienteFiscalLectura(_cliente, Xexx, "Cliente del exterior", "616", "97000", "S01", "03", "PUE", "USD", true,
            NumRegIdTrib: "130044147", PaisResidencia: "DOM");
        var validador = new Millet.Facturacion.Application.Timbrado.ValidadorReceptorFiscal(
            new FakeClientesReadPort(maestro), new FakeCatalogosSatReadPort(), db);
        await new EmitirFacturaAnticipoHandler(db, new FakeSender(new ReservarFolioResponse("FANT-000002", 2, "")),
            new FakePeriodoContablePort(), new FakeCatalogosSatReadPort(tipoCambio: 18m), new FakeFiscalApiClient(), new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(_empresa, "MIL010101AAA", "Millet", "601", 0.16m, "97000")),
            new FakeIntegrationEventPublisher(), new FakeEmpresaContext(_empresa), new FakeUserContext(Guid.NewGuid()),
            new FakeClock(Ahora), validador, new FakeClientesReadPort(maestro))
            .Handle(new EmitirFacturaAnticipoCommand(Guid.NewGuid(), _cliente, Xexx, "Cliente del exterior", "616", "97000", "S01",
                "DOM", "MIL010101AAA", "601", "PUE", "03", "USD", 18m, TipoAnticipo.ClientesUsd, 1000m, 0m, null, null, null, null, null),
                default);
        return await db.Anticipos.Include(a => a.Vinculaciones).SingleAsync();
    }

    private async Task<FacturaVenta> FacturaExportacionAsync(FacturacionDbContext db, string? uuidAnticipo)
    {
        var fv = FacturaVenta.CrearBorrador(_empresa, "FEXP-000011", 11, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor(Xexx, "Cliente del exterior", "616", "97000", "S01", "DOM", true),
            new DatosFiscalesEmisor("MIL010101AAA", "Millet", "601", "97000"), "PPD", "99", "USD", 18m,
            2026, 10, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "30171600", "Ventana", "H87", 1m, 2000m, 0m, "02", 0m, null, null);
        fv.RecalcularTotales();
        if (uuidAnticipo is not null) fv.AgregarRelacion(uuidAnticipo, "07");
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado("88888888-8888-8888-8888-888888888888", null, null, null, Ahora, null, null);
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();
        return fv;
    }

    [Fact]
    public async Task Exportacion_aplicada_despues_sale_con_iva_tasa_cero_forma_30_uso_g02_y_tc_del_dia()
    {
        using var db = Db();
        var anticipo = await AnticipoExtranjeroAsync(db);
        var cfdiAnticipo = await db.FacturasAnticipo.SingleAsync();
        cfdiAnticipo.ReceptorNumRegIdTrib.Should().Be("130044147");
        cfdiAnticipo.TasaIvaTraslado.Should().Be(0m);
        var factura = await FacturaExportacionAsync(db, cfdiAnticipo.Uuid);

        await Handler(db).Handle(new AplicarAnticipoAFacturaCommand(anticipo.Id, factura.Id, 194.39m), default);

        var nc = await db.NotasCredito.Include(n => n.Relaciones).SingleAsync();
        (nc.FormaPago, nc.MetodoPago, nc.ReceptorUsoCfdi, nc.TipoCambio).Should().Be(("30", "PUE", "G02", 18.7592m));
        (nc.Subtotal, nc.ImpuestosTrasladados, nc.Total).Should().Be((194.39m, 0m, 194.39m));
        nc.ReceptorNumRegIdTrib.Should().Be("130044147");
        nc.Relaciones.Should().ContainSingle(r => r.TipoRelacion == "07" && r.UuidRelacionado == factura.Uuid);
        var concepto = Millet.Facturacion.Application.Timbrado.CfdiEmisionBuilder.DesdeNotaCredito(nc, Ahora).Conceptos.Single();
        concepto.Impuestos.Single().Should().Match<Millet.Integraciones.Fiscal.Domain.Ports.ImpuestoConceptoCfdi>(i =>
            i.TipoFactor == "Tasa" && i.TasaOCuota == 0m && i.Importe == 0m);
    }

    [Fact]
    public async Task Con_rfc_generico_sin_pedido_ni_relacion_07_no_se_aplica()
    {
        using var db = Db();
        var anticipo = await AnticipoExtranjeroAsync(db);
        var factura = await FacturaExportacionAsync(db, uuidAnticipo: null);

        var act = () => Handler(db).Handle(new AplicarAnticipoAFacturaCommand(anticipo.Id, factura.Id, 100m), default);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_CLIENTE_NO_VERIFICABLE");
    }

    [Fact]
    public async Task La_nc_nacional_aplicada_despues_sigue_con_iva_16_y_sin_tipo_de_cambio()
    {
        using var db = Db();
        var anticipo = await AnticipoAsync(db);
        var factura = await FacturaAsync(db);

        await Handler(db).Handle(new AplicarAnticipoAFacturaCommand(anticipo.Id, factura.Id, 580m), default);

        var nc = await db.NotasCredito.SingleAsync();
        (nc.Subtotal, nc.ImpuestosTrasladados, nc.TipoCambio, nc.FormaPago).Should().Be((500m, 80m, (decimal?)null, "30"));
    }

    [Fact]
    public async Task Aplica_el_anticipo_a_una_factura_timbrada_con_nc_07_y_baja_el_saldo_del_anticipo()
    {
        using var db = Db();
        var anticipo = await AnticipoAsync(db);
        var factura = await FacturaAsync(db);

        var resp = await Handler(db).Handle(new AplicarAnticipoAFacturaCommand(anticipo.Id, factura.Id, 580m), default);

        resp.Importe.Should().Be(580m);
        resp.SaldoAnticipo.Should().Be(580m);
        resp.SaldoFactura.Should().Be(2320m - 580m);
        var nc = await db.NotasCredito.Include(n => n.Relaciones).SingleAsync();
        nc.Motivo.Should().Be(MotivoNotaCredito.Amortizacion);
        nc.FacturaRelacionadaId.Should().Be(factura.Id);
        nc.AnticipoOrigenId.Should().Be(anticipo.Id);
        nc.Relaciones.Should().ContainSingle(r => r.TipoRelacion == "07" && r.UuidRelacionado == factura.Uuid); // solo la factura de venta
        var evento = _eventos.Publicados.OfType<NotaCreditoTimbradaIntegrationEvent>().Single();
        evento.FacturaRelacionadaId.Should().Be(factura.Id, "CxC la descuenta del saldo de la factura");
        evento.AnticipoOrigenId.Should().Be(anticipo.Id);
        evento.ClienteId.Should().Be(_cliente);
        (await db.Anticipos.SingleAsync()).MontoAmortizado.Should().Be(580m);
    }

    [Fact]
    public async Task Lo_apartado_con_vincular_se_convierte_en_aplicacion_por_el_mismo_importe()
    {
        using var db = Db();
        var anticipo = await AnticipoAsync(db);
        var factura = await FacturaAsync(db);
        anticipo.Vincular(factura.Id, 300m, Ahora);
        await db.SaveChangesAsync();

        var otroImporte = () => Handler(db).Handle(new AplicarAnticipoAFacturaCommand(anticipo.Id, factura.Id, 500m), default);
        await otroImporte.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_IMPORTE_APARTADO");

        await Handler(db).Handle(new AplicarAnticipoAFacturaCommand(anticipo.Id, factura.Id, 300m), default);
        var actual = await db.Anticipos.Include(a => a.Vinculaciones).SingleAsync();
        actual.Vinculaciones.Should().ContainSingle().Which.NcAmortizacionId.Should().NotBeNull();
        actual.SaldoDisponible.Should().Be(860m, "lo apartado ya no bloquea el saldo: se aplicó");
    }

    [Fact]
    public async Task No_aplica_mas_que_el_saldo_pendiente_de_la_factura_segun_cxc()
    {
        using var db = Db();
        var anticipo = await AnticipoAsync(db);
        var factura = await FacturaAsync(db);

        var act = () => Handler(db, saldoCxc: 200m).Handle(new AplicarAnticipoAFacturaCommand(anticipo.Id, factura.Id, 300m), default);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_EXCEDE_SALDO_FACTURA");
        (await db.NotasCredito.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task No_aplica_mas_que_el_saldo_disponible_del_anticipo()
    {
        using var db = Db();
        var anticipo = await AnticipoAsync(db);
        var factura = await FacturaAsync(db);

        var act = () => Handler(db).Handle(new AplicarAnticipoAFacturaCommand(anticipo.Id, factura.Id, 1160.01m), default);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_SALDO_INSUFICIENTE");
    }

    [Fact]
    public async Task Rechaza_otro_cliente_u_otra_moneda()
    {
        using var db = Db();
        var anticipo = await AnticipoAsync(db);
        var otroCliente = await FacturaAsync(db, rfc: "BBB020202BBB");

        var act = () => Handler(db).Handle(new AplicarAnticipoAFacturaCommand(anticipo.Id, otroCliente.Id, 100m), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_CLIENTE_DISTINTO");

        using var db2 = Db();
        var anticipoMxn = await AnticipoAsync(db2);
        var facturaUsd = await FacturaAsync(db2, moneda: "USD");
        var act2 = () => Handler(db2).Handle(new AplicarAnticipoAFacturaCommand(anticipoMxn.Id, facturaUsd.Id, 100m), default);
        await act2.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_MONEDA_DISTINTA");
    }

    [Fact]
    public async Task La_bonificacion_no_acredita_mas_que_el_saldo_pendiente_de_la_factura()
    {
        using var db = Db();
        var factura = await FacturaAsync(db);
        var handler = new EmitirNotaCreditoBonificacionHandler(db, new FakeSender(new ReservarFolioResponse("NC-000030", 30, "")),
            new FakePeriodoContablePort(), new FakeFiscalApiClient(), new FakeCfdiRepositorioPort(), _eventos,
            new FakeEmpresaContext(_empresa), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora),
            ReceptorFiscalTestFactory.Crear(db), new FakeCurrentUserPermissions(), new FakeSaldoFacturaReadPort(1000m));

        var act = () => handler.Handle(new EmitirNotaCreditoBonificacionCommand(factura.Id, 1000.01m, 0.16m, null), default);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "NC_IMPORTE_MAYOR_SALDO");
        (await db.NotasCredito.AnyAsync()).Should().BeFalse();
    }
}
