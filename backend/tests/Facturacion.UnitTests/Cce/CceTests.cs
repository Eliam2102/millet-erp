using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Domain.Cce;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cce;

public sealed class CceTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    // ---- Dominio ----

    [Fact]
    public void CrearCce_valida_incoterm_tc_y_taxid()
    {
        var act = () => ComplementoCce.Crear(Guid.NewGuid(), "2", "", 20m, "TAX-1", "USA");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_INCOTERM_INVALIDO");

        var act2 = () => ComplementoCce.Crear(Guid.NewGuid(), "2", "FOB", 0m, "TAX-1", "USA");
        act2.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_TC_DOF_INVALIDO");

        var act3 = () => ComplementoCce.Crear(Guid.NewGuid(), "2", "FOB", 20m, "", "USA");
        act3.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_RECEPTOR_TAXID_INVALIDO");
    }

    [Fact]
    public void CrearCce_y_agregar_mercancia()
    {
        var cce = ComplementoCce.Crear(Guid.NewGuid(), "2", "FOB", 20.5m, "TAX-USA-1", "usa");
        cce.AgregarLinea("84713001", "01", 1500m, 2.5m, 3750m, aplicaIva0: true);

        cce.ReceptorPaisResidencia.Should().Be("USA");
        cce.Lineas.Should().ContainSingle(l => l.AplicaIva0 && l.FraccionArancelaria == "84713001");
    }

    [Fact]
    public void AgregarLinea_bien_tangible_valida_fraccion_trio_y_valor_usd()
    {
        var cce = ComplementoCce.Crear(Guid.NewGuid(), "2", "FOB", 20m, "TAX-1", "USA");

        // Fracción vacía o con formato inválido (no 8-10 dígitos).
        cce.Invoking(c => c.AgregarLinea("", "01", 10m, 2m, 20m, false))
            .Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_FRACCION_INVALIDA");
        cce.Invoking(c => c.AgregarLinea("7007", "01", 10m, 2m, 20m, false))
            .Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_FRACCION_FORMATO");
        cce.Invoking(c => c.AgregarLinea("7007AB00", "01", 10m, 2m, 20m, false))
            .Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_FRACCION_FORMATO");

        // Trío aduanero incompleto.
        cce.Invoking(c => c.AgregarLinea("70071100", "", 10m, 2m, 20m, false))
            .Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_UNIDAD_ADUANA_INVALIDA");
        cce.Invoking(c => c.AgregarLinea("70071100", "01", 0m, 2m, 20m, false))
            .Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_CANTIDAD_INVALIDA");
        cce.Invoking(c => c.AgregarLinea("70071100", "01", 10m, 0m, 20m, false))
            .Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_VALOR_UNITARIO_ADUANA_INVALIDO");

        // Valor USD siempre requerido.
        cce.Invoking(c => c.AgregarLinea("70071100", "01", 10m, 2m, 0m, false))
            .Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_VALOR_DOLARES_INVALIDO");

        // Línea válida: fracción de 10 dígitos (fracción + NICO) también acepta.
        cce.AgregarLinea("7007110099", "01", 10m, 2m, 20m, aplicaIva0: true);
        cce.Lineas.Should().ContainSingle();
    }

    [Fact]
    public void AgregarLinea_servicio_unidad_99_prohibe_fraccion_pero_exige_valor_usd()
    {
        var cce = ComplementoCce.Crear(Guid.NewGuid(), "2", "FOB", 20m, "TAX-1", "USA");

        // Unidad 99 (sin unidad, servicio) CON fracción → prohibida (CCE159).
        cce.Invoking(c => c.AgregarLinea("70071100", "99", 0m, 0m, 20m, false))
            .Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_FRACCION_NO_APLICA");

        // Sin fracción exime el trío, pero el valor USD sigue siendo obligatorio.
        cce.Invoking(c => c.AgregarLinea("", "99", 0m, 0m, 0m, false))
            .Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_VALOR_DOLARES_INVALIDO");

        cce.AgregarLinea("", "99", 0m, 0m, 15m, aplicaIva0: false);
        cce.Lineas.Should().ContainSingle(l => l.UnidadAduana == "99");
    }

    private static FacturaVenta FacturaExportacionBorrador()
    {
        var receptor = new DatosFiscalesReceptor("XEXX010101000", "Foreign Corp", "616", "00000", "S01", "USA", true);
        return FacturaVenta.CrearBorrador(Guid.NewGuid(), "F-1", 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "01", "USD", 20m, 2026, 5,
            (short)8, ComportamientoFiscal.ExportacionConCce, null, null, null, false);
    }

    [Fact]
    public void AdjuntarCce_a_factura_no_exportacion_lanza()
    {
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(Guid.NewGuid(), "F-1", 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "01", "MXN", null, 2026, 5,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        var cce = ComplementoCce.Crear(fv.Id, "2", "FOB", 20m, "TAX-1", "USA");

        var act = () => fv.AdjuntarCce(cce);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_COMPORTAMIENTO_INVALIDO");
    }

    [Fact]
    public void AdjuntarCce_a_exportacion_ok()
    {
        var fv = FacturaExportacionBorrador();
        var cce = ComplementoCce.Crear(fv.Id, "2", "FOB", 20m, "TAX-1", "USA");

        fv.AdjuntarCce(cce);

        fv.ComplementoCce.Should().NotBeNull();
    }

    // ---- Handler ----

    private static EmitirFacturaVentaHandler Handler(FacturacionDbContext db, Guid empresaId) =>
        new(db, new FakeSender(new ReservarFolioResponse("EXP-000001", 1, "")),
            new FakePeriodoContablePort(), new FakeCatalogosSatReadPort(), new FakeFiscalApiClient(),
            new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120")),
            new FakeIntegrationEventPublisher(), new FakeContabilidadAsientoPort(), new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));

    private static EmitirFacturaVentaCommand CommandExportacion(EmitirFacturaVentaCce? cce) => new(
        SucursalId: Guid.NewGuid(),
        ReceptorRfc: "XEXX010101000", ReceptorNombre: "Foreign Corp", ReceptorRegimenFiscal: "616",
        ReceptorCodigoPostal: "00000", ReceptorUsoCfdi: "S01", ReceptorPais: "USA",
        RfcEmisor: "BBB010101BBB", RegimenFiscalEmisor: "601",
        MetodoPago: "PUE", FormaPago: "01", Moneda: "USD", TipoCambio: 20m,
        CanalVenta: (short)8, ComportamientoFiscal: ComportamientoFiscal.ExportacionConCce,
        ObraId: null, ObraNombre: null, FacturaAgrupada: false,
        Lineas: [new EmitirFacturaVentaLinea(null, "84713001", "Export goods", "H87", 1m, 3750m, 0m, "02", 0m, null, null)],
        Anticipos: null,
        Cce: cce);

    [Fact]
    public async Task Emitir_factura_exportacion_con_CCE_persiste_el_complemento()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var cce = new EmitirFacturaVentaCce("2", "FOB", 20.5m, "TAX-USA-1", "USA",
            [new EmitirFacturaVentaCceLinea("84713001", "01", 1500m, 2.5m, 3750m, true)],
            ClaveDePedimento: "A1", CertificadoOrigen: true,
            ReceptorDomicilioCalle: "123 Main St", ReceptorDomicilioEstado: "TX",
            ReceptorDomicilioCodigoPostal: "75001");

        var resp = await Handler(db, empresaId).Handle(CommandExportacion(cce), CancellationToken.None);

        resp.Estado.Should().Be("Timbrado");
        var factura = await db.FacturasVenta.Include(f => f.ComplementoCce!).ThenInclude(c => c.Lineas).SingleAsync();
        factura.ComplementoCce.Should().NotBeNull();
        factura.ComplementoCce!.Incoterm.Should().Be("FOB");
        factura.ComplementoCce!.TcDof.Should().Be(20.5m);
        factura.ComplementoCce!.ClaveDePedimento.Should().Be("A1");
        factura.ComplementoCce!.CertificadoOrigen.Should().BeTrue();
        factura.ComplementoCce!.ReceptorDomicilioEstado.Should().Be("TX");
        factura.ComplementoCce!.ReceptorDomicilioCodigoPostal.Should().Be("75001");
        factura.ComplementoCce!.Lineas.Should().ContainSingle(l => l.AplicaIva0);
    }

    [Fact]
    public async Task Emitir_con_CCE_sin_domicilio_falla_pre_vuelo_sin_timbrar()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        // Sin ReceptorDomicilioEstado/CodigoPostal → el builder aborta antes del PAC.
        var cce = new EmitirFacturaVentaCce("2", "FOB", 20.5m, "TAX-USA-1", "USA",
            [new EmitirFacturaVentaCceLinea("84713001", "01", 1500m, 2.5m, 3750m, true)]);

        var act = () => Handler(db, empresaId).Handle(CommandExportacion(cce), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .Where(e => e.Code == "CCE_DOMICILIO_RECEPTOR_INCOMPLETO");
        (await db.FacturasVenta.AnyAsync()).Should().BeFalse(); // nada persistido
    }

    [Fact]
    public async Task Emitir_factura_exportacion_sin_CCE_no_crea_complemento()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        await Handler(db, empresaId).Handle(CommandExportacion(null), CancellationToken.None);

        (await db.FacturasVenta.Include(f => f.ComplementoCce).SingleAsync()).ComplementoCce.Should().BeNull();
    }
}
