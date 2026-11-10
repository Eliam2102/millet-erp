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
        cce.AgregarLinea("8471300100", "01", 1500m, 2.5m, 3750m, aplicaIva0: true);

        cce.ReceptorPaisResidencia.Should().Be("USA");
        cce.Lineas.Should().ContainSingle(l => l.AplicaIva0 && l.FraccionArancelaria == "8471300100");
    }

    [Fact]
    public void AgregarLinea_bien_tangible_valida_fraccion_trio_y_valor_usd()
    {
        var cce = ComplementoCce.Crear(Guid.NewGuid(), "2", "FOB", 20m, "TAX-1", "USA");

        // Fracción vacía o con formato inválido (M-158: exactamente 10 dígitos, fracción + NICO).
        cce.Invoking(c => c.AgregarLinea("", "01", 10m, 2m, 20m, false))
            .Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_FRACCION_INVALIDA");
        cce.Invoking(c => c.AgregarLinea("7007", "01", 10m, 2m, 20m, false))
            .Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_FRACCION_INVALIDA");
        cce.Invoking(c => c.AgregarLinea("7007AB00", "01", 10m, 2m, 20m, false))
            .Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_FRACCION_INVALIDA");
        cce.Invoking(c => c.AgregarLinea("70071100", "01", 10m, 2m, 20m, false))
            .Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_FRACCION_INVALIDA" && e.Message.Contains("10 dígitos"));

        // Trío aduanero incompleto.
        cce.Invoking(c => c.AgregarLinea("7007110000", "", 10m, 2m, 20m, false))
            .Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_UNIDAD_ADUANA_INVALIDA");
        cce.Invoking(c => c.AgregarLinea("7007110000", "01", 0m, 2m, 20m, false))
            .Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_CANTIDAD_INVALIDA");
        cce.Invoking(c => c.AgregarLinea("7007110000", "01", 10m, 0m, 20m, false))
            .Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_VALOR_UNITARIO_ADUANA_INVALIDO");

        // Valor USD siempre requerido.
        cce.Invoking(c => c.AgregarLinea("7007110000", "01", 10m, 2m, 0m, false))
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
        cce.Invoking(c => c.AgregarLinea("7007110000", "99", 0m, 0m, 20m, false))
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

    [Fact]
    public void Copia_embarque_json_anterior_deserializa_sin_aduana()
    {
        const string json = """{"FolioExpediente":"CE-DEMO","NumeroPedimento":"DEMO-PED","ClavePedimento":"A1","FechaPedimento":"2026-10-12","Incoterm":"DDP","Contenedores":[]}""";
        var datos = System.Text.Json.JsonSerializer.Deserialize<DatosEmbarqueCe>(json)!;
        Assert.Null(datos.Aduana); Assert.Null(datos.AduanaNombre);
    }

    [Fact]
    public void Tomar_embarque_conserva_aduana_en_copia_inmutable()
    {
        var f = FacturaExportacionBorrador();
        f.TomarEmbarqueCe(new("CE-DEMO", "DEMO-PED", "A1", new(2026, 10, 12), "DDP", [], "16", "Manzanillo"));
        Assert.Equal("16", f.DatosEmbarqueCe!.Aduana);
        Assert.Equal("Manzanillo", f.DatosEmbarqueCe.AduanaNombre);
    }

    // ---- Handler ----

    private static EmitirFacturaVentaHandler Handler(FacturacionDbContext db, Guid empresaId, MediatR.ISender? sender = null) =>
        new(db, sender ?? new FakeSender(new ReservarFolioResponse("EXP-000001", 1, "")),
            new FakePeriodoContablePort(), new FakeCatalogosSatReadPort(), new FakeFiscalApiClient(),
            new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120")),
            new FakeIntegrationEventPublisher(), new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(db));

    private static EmitirFacturaVentaCommand CommandExportacion(EmitirFacturaVentaCce? cce) => new(
        SucursalId: Guid.NewGuid(),
        ReceptorRfc: "XEXX010101000", ReceptorNombre: "Foreign Corp", ReceptorRegimenFiscal: "616",
        ReceptorCodigoPostal: "76120", ReceptorUsoCfdi: "S01", ReceptorPais: "USA",
        RfcEmisor: "BBB010101BBB", RegimenFiscalEmisor: "601",
        MetodoPago: "PUE", FormaPago: "01", Moneda: "USD", TipoCambio: 20m,
        CanalVenta: (short)8, ComportamientoFiscal: ComportamientoFiscal.ExportacionConCce,
        ObraId: null, ObraNombre: null, FacturaAgrupada: false,
        Lineas: [new EmitirFacturaVentaLinea(null, "8471300100", "Export goods", "H87", 1m, 3750m, 0m, "02", 0m, null, null)],
        Anticipos: null,
        Cce: cce);

    [Fact]
    public async Task Emitir_factura_exportacion_con_CCE_persiste_el_complemento()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var cce = new EmitirFacturaVentaCce("2", "FOB", 20.5m, "TAX-USA-1", "USA",
            [new EmitirFacturaVentaCceLinea("8471300100", "01", 1500m, 2.5m, 3750m, true)],
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
            [new EmitirFacturaVentaCceLinea("8471300100", "01", 1500m, 2.5m, 3750m, true)]);

        var act = () => Handler(db, empresaId).Handle(CommandExportacion(cce), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .Where(e => e.Code == "CCE_DOMICILIO_RECEPTOR_INCOMPLETO");
        // Ninguna factura vigente: si el folio ya se había reservado, queda Descartada (B2).
        (await db.FacturasVenta.AnyAsync(f => f.Estado != EstadoTimbrado.Descartada)).Should().BeFalse();
    }

    [Fact]
    public async Task Emitir_factura_exportacion_sin_CCE_rechaza_identidad_extranjera_incompleta()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var act = () => Handler(db, empresaId).Handle(CommandExportacion(null), CancellationToken.None);
        var error = await act.Should().ThrowAsync<Millet.Facturacion.Application.Timbrado.ReceptorFiscalInvalidoException>();
        error.Which.Campos.Select(c => c.Campo).Should().Contain(["numRegIdTrib", "paisResidencia"]);
        (await db.FacturasVenta.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Emitir_exportacion_sin_calle_del_receptor_se_rechaza_antes_de_reservar_folio_B5()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderCuentaFolios();
        var cce = new EmitirFacturaVentaCce("2", "FOB", 20.5m, "TAX-USA-1", "USA",
            [new EmitirFacturaVentaCceLinea("8471300100", "01", 1500m, 2.5m, 3750m, true)],
            ClaveDePedimento: "A1", ReceptorDomicilioEstado: "TX", ReceptorDomicilioCodigoPostal: "75001");

        var act = () => Handler(db, empresaId, sender).Handle(CommandExportacion(cce), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CCE_DOMICILIO_RECEPTOR_INCOMPLETO");
        sender.FoliosReservados.Should().Be(0, "un dato corregible no debe consumir folio");
        (await db.FacturasVenta.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Emitir_exportacion_sin_clave_de_pedimento_se_rechaza_antes_de_reservar_folio_B5()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderCuentaFolios();
        var cce = new EmitirFacturaVentaCce("2", "FOB", 20.5m, "TAX-USA-1", "USA",
            [new EmitirFacturaVentaCceLinea("8471300100", "01", 1500m, 2.5m, 3750m, true)],
            ReceptorDomicilioCalle: "123 Main St", ReceptorDomicilioEstado: "TX", ReceptorDomicilioCodigoPostal: "75001");

        var act = () => Handler(db, empresaId, sender).Handle(CommandExportacion(cce), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CCE_CLAVE_PEDIMENTO_REQUERIDA");
        sender.FoliosReservados.Should().Be(0);
    }

    private sealed class FakeSenderCuentaFolios : MediatR.ISender
    {
        public int FoliosReservados { get; private set; }
        public Task<TResponse> Send<TResponse>(MediatR.IRequest<TResponse> request, CancellationToken ct = default)
        {
            if (request is ReservarFolioCommand)
            {
                FoliosReservados++;
                return Task.FromResult((TResponse)(object)new ReservarFolioResponse("EXP-000001", 1, ""));
            }
            throw new NotImplementedException();
        }
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : MediatR.IRequest => throw new NotImplementedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(MediatR.IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
