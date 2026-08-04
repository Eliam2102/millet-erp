using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Anticipos.EmitirFacturaAnticipo;
using Millet.Facturacion.Application.Anticipos.VincularAnticipo;
using Millet.Facturacion.Application.Reportes.ControlAnticipos;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Anticipos;

public sealed class AnticiposHandlerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
    private const string RfcCliente = "AAA010101AAA";

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private static EmitirFacturaAnticipoCommand Command(string metodoPago = "PUE", decimal montoBase = 1000m, string rfc = RfcCliente) => new(
        SucursalId: Guid.NewGuid(),
        ClienteId: Guid.NewGuid(),
        ReceptorRfc: rfc,
        ReceptorNombre: "Cliente Maquila",
        ReceptorRegimenFiscal: "601",
        ReceptorCodigoPostal: "97000",
        ReceptorUsoCfdi: "G03",
        ReceptorPais: "MEX",
        RfcEmisor: "BBB010101BBB",
        RegimenFiscalEmisor: "601",
        MetodoPago: metodoPago,
        FormaPago: "03",
        Moneda: "MXN",
        TipoCambio: null,
        TipoAnticipo: TipoAnticipo.ClientesMxp,
        MontoBase: montoBase,
        TasaIvaTraslado: 0.16m,
        Descripcion: null,
        PedidoFacturableId: null,
        PedidoOrigenRef: "AW-42",
        ObraId: null,
        ObraNombre: "Obra Norte");

    private static EmitirFacturaAnticipoHandler EmitirHandler(FacturacionDbContext db, Guid empresaId, bool periodoAbierto = true) =>
        new(
            db,
            new FakeSender(new ReservarFolioResponse("FANT-000001", 1, "")),
            new FakePeriodoContablePort(periodoAbierto),
            new FakeCatalogosSatReadPort(),
            new FakeFiscalApiClient(),
            new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120")),
            new FakeIntegrationEventPublisher(),
            new FakeEmpresaContext(empresaId),
            new FakeUserContext(Guid.NewGuid()),
            new FakeClock(Ahora));

    /// <summary>Siembra una FacturaVenta timbrada del RFC indicado para poder vincular.</summary>
    private static async Task<FacturaVenta> SembrarFacturaVentaTimbradaAsync(
        FacturacionDbContext db, Guid empresaId, string rfc = RfcCliente, EstadoTimbrado estado = EstadoTimbrado.Timbrado)
    {
        var receptor = new DatosFiscalesReceptor(rfc, "Cliente Maquila", "601", "97000", "G03", "MEX",
            DatosFiscalesReceptor.EsRfcGenerico(rfc));
        var fv = FacturaVenta.CrearBorrador(empresaId, "F-000001", 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "03", "MXN", null, 2026, 5,
            (short)7, ComportamientoFiscal.ConAnticipo, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "Servicio maquila", "E48", 1m, 5000m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        if (estado is EstadoTimbrado.Timbrado)
        {
            fv.MarcarTimbradoEnProceso();
            fv.MarcarTimbrado("11111111-1111-1111-1111-111111111111", null, null, null, Ahora, null, null);
        }
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();
        return fv;
    }

    // ---- Emisión (M1) ----

    [Fact]
    public async Task Emitir_PUE_timbra_y_abre_saldo_por_el_total()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var resp = await EmitirHandler(db, empresaId).Handle(Command(), CancellationToken.None);

        resp.Estado.Should().Be("Timbrado");
        resp.Uuid.Should().NotBeNullOrEmpty();
        resp.Folio.Should().Be("FANT-000001");
        resp.Total.Should().Be(1160m);
        resp.Saldo.Should().Be(1160m);

        var anticipo = await db.Anticipos.SingleAsync();
        anticipo.Estado.Should().Be(EstadoAnticipo.Abierto);
        anticipo.MontoCobrado.Should().Be(1160m);
        anticipo.FacturaAnticipoId.Should().Be(resp.FacturaAnticipoId);
        anticipo.PedidoOrigenRef.Should().Be("AW-42");

        var factura = await db.FacturasAnticipo.SingleAsync();
        factura.AnticipoId.Should().Be(anticipo.Id);
        factura.CfdiArchivoId.Should().NotBeNull();
    }

    [Fact]
    public async Task Emitir_PPD_abre_saldo_en_cero()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var resp = await EmitirHandler(db, empresaId).Handle(Command(metodoPago: "PPD"), CancellationToken.None);

        resp.Saldo.Should().Be(0m);
        (await db.Anticipos.SingleAsync()).MontoCobrado.Should().Be(0m);
    }

    [Fact]
    public async Task Emitir_a_RFC_generico_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var act = () => EmitirHandler(db, empresaId).Handle(Command(rfc: "XAXX010101000"), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_RECEPTOR_GENERICO");
    }

    [Fact]
    public async Task Emitir_con_periodo_cerrado_lanza_PERIODO_CERRADO()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var act = () => EmitirHandler(db, empresaId, periodoAbierto: false).Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PERIODO_CERRADO");
    }

    // ---- Vinculación (M2) ----

    [Fact]
    public async Task Vincular_compromete_el_disponible_sin_amortizar()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var emit = await EmitirHandler(db, empresaId).Handle(Command(), CancellationToken.None);
        var factura = await SembrarFacturaVentaTimbradaAsync(db, empresaId);

        var resp = await new VincularAnticipoHandler(db, new FakeClock(Ahora))
            .Handle(new VincularAnticipoCommand(emit.AnticipoId, factura.Id, 400m), CancellationToken.None);

        resp.Saldo.Should().Be(1160m);          // M2 no amortiza
        resp.SaldoDisponible.Should().Be(760m); // pero compromete
        (await db.Anticipos.Include(a => a.Vinculaciones).SingleAsync()).Vinculaciones.Should().HaveCount(1);
    }

    [Fact]
    public async Task Vincular_mas_que_el_saldo_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var emit = await EmitirHandler(db, empresaId).Handle(Command(), CancellationToken.None);
        var factura = await SembrarFacturaVentaTimbradaAsync(db, empresaId);

        var act = () => new VincularAnticipoHandler(db, new FakeClock(Ahora))
            .Handle(new VincularAnticipoCommand(emit.AnticipoId, factura.Id, 5000m), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_SALDO_INSUFICIENTE");
    }

    [Fact]
    public async Task Vincular_a_factura_de_otro_cliente_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var emit = await EmitirHandler(db, empresaId).Handle(Command(), CancellationToken.None);
        var factura = await SembrarFacturaVentaTimbradaAsync(db, empresaId, rfc: "CCC010101CCC");

        var act = () => new VincularAnticipoHandler(db, new FakeClock(Ahora))
            .Handle(new VincularAnticipoCommand(emit.AnticipoId, factura.Id, 100m), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_CLIENTE_DISTINTO");
    }

    [Fact]
    public async Task Vincular_a_factura_no_timbrada_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var emit = await EmitirHandler(db, empresaId).Handle(Command(), CancellationToken.None);
        var factura = await SembrarFacturaVentaTimbradaAsync(db, empresaId, estado: EstadoTimbrado.Borrador);

        var act = () => new VincularAnticipoHandler(db, new FakeClock(Ahora))
            .Handle(new VincularAnticipoCommand(emit.AnticipoId, factura.Id, 100m), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "FACTURA_NO_TIMBRADA");
    }

    [Fact]
    public async Task Vincular_anticipo_inexistente_lanza_NotFound()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var act = () => new VincularAnticipoHandler(db, new FakeClock(Ahora))
            .Handle(new VincularAnticipoCommand(Guid.NewGuid(), Guid.NewGuid(), 100m), CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>().Where(e => e.Code == "ANTICIPO_NO_ENCONTRADO");
    }

    // ---- Reporte Control de Anticipos ----

    [Fact]
    public async Task ControlAnticipos_resume_saldos_con_totales()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        await EmitirHandler(db, empresaId).Handle(Command(), CancellationToken.None);

        var reporte = await new ControlAnticiposResumenHandler(db, new FakeClock(Ahora))
            .Handle(new ControlAnticiposResumenQuery(null, null, null, null, null), CancellationToken.None);

        reporte.Titulo.Should().Be("Control de Anticipos");
        reporte.Filas.Should().HaveCount(1);
        reporte.Filas[0].Folio.Should().Be("FANT-000001");
        reporte.Filas[0].Saldo.Should().Be(1160m);
        reporte.Filas[0].Estado.Should().Be("Abierto");
        reporte.Totales!["saldo"].Should().Be(1160m);
        reporte.Totales!["montoCobrado"].Should().Be(1160m);
    }

    [Fact]
    public async Task ControlAnticipos_filtra_por_estado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        await EmitirHandler(db, empresaId).Handle(Command(), CancellationToken.None);

        var reporte = await new ControlAnticiposResumenHandler(db, new FakeClock(Ahora))
            .Handle(new ControlAnticiposResumenQuery(null, EstadoAnticipo.Amortizado, null, null, null), CancellationToken.None);

        reporte.Filas.Should().BeEmpty();
    }
}
