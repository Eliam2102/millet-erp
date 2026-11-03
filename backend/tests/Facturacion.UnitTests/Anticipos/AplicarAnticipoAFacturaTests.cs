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
            new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));

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
        nc.Relaciones.Should().HaveCount(2).And.OnlyContain(r => r.TipoRelacion == "07");
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
