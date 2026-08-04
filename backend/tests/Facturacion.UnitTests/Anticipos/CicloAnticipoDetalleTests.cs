using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Anticipos.EmitirFacturaAnticipo;
using Millet.Facturacion.Application.Anticipos.Queries;
using Millet.Facturacion.Application.Anticipos.VincularAnticipo;
using Millet.Facturacion.Application.Comprobantes.Queries;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Pdf;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Anticipos;

/// <summary>
/// Doc 13 (anticipos ciclo completo): detalle enriquecido (13-A), bandeja con
/// saldo (13-H), descargas genéricas (13-B/13-C) y fix 13-J — la relación 07
/// al CFDI del anticipo es obligatoria (caso VEN-000003: dos anticipos, solo
/// uno relacionado porque el otro no estaba timbrado).
/// </summary>
public sealed class CicloAnticipoDetalleTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
    private const string RfcCliente = "AAA010101AAA";

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private sealed class FakeSenderFoliosUnicos : ISender
    {
        private int _n;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            if (request is ReservarFolioCommand)
            {
                _n++;
                return Task.FromResult((TResponse)(object)new ReservarFolioResponse($"DOC-{_n:D6}", _n, ""));
            }
            throw new NotImplementedException();
        }
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotImplementedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeFiscalUuidsUnicos : ICfdiTimbradoPort
    {
        private int _n;
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct)
        {
            _n++;
            return Task.FromResult(new TimbradoResultado(
                TimbradoEstado.Timbrado, $"00000000-0000-0000-0000-{_n:D12}", "S", "S", "0",
                Ahora, "SAT970701NN3", $"<cfdi serie=\"{emision.Serie}\" folio=\"{emision.Folio}\" fake=\"true\" />", null, null));
        }
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) =>
            Task.FromResult(new CancelacionResultado(CancelacionEstado.Aceptada, "ok", null));
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) =>
            Task.FromResult(new EstatusCfdiResultado(true, "Vigente", true, null));
    }

    private sealed class FakeFiscalSiempreFalla : ICfdiTimbradoPort
    {
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct) =>
            Task.FromResult(new TimbradoResultado(
                TimbradoEstado.Fallido, null, null, null, null, null, null, null, "CFDI40100", "Rechazo del PAC"));
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) =>
            Task.FromResult(new CancelacionResultado(CancelacionEstado.Aceptada, "ok", null));
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) =>
            Task.FromResult(new EstatusCfdiResultado(true, "Vigente", true, null));
    }

    private static FakeEmpresaFiscalReadPort EmpresasFiscal(Guid empresaId) =>
        new(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120"));

    private static EmitirFacturaAnticipoHandler EmitirAnticipoHandler(
        FacturacionDbContext db, Guid empresaId, ISender sender, ICfdiTimbradoPort fiscal) =>
        new(db, sender, new FakePeriodoContablePort(), new FakeCatalogosSatReadPort(), fiscal,
            new FakeCfdiRepositorioPort(), EmpresasFiscal(empresaId), new FakeIntegrationEventPublisher(),
            new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));

    private static EmitirFacturaVentaHandler FacturaHandler(
        FacturacionDbContext db, Guid empresaId, ISender sender, ICfdiTimbradoPort fiscal) =>
        new(db, sender, new FakePeriodoContablePort(), new FakeCatalogosSatReadPort(), fiscal,
            new FakeCfdiRepositorioPort(), EmpresasFiscal(empresaId), new FakeIntegrationEventPublisher(),
            new FakeContabilidadAsientoPort(), new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));

    private static EmitirFacturaAnticipoCommand AnticipoCommand(decimal montoBase = 1000m) => new(
        Guid.NewGuid(), Guid.NewGuid(), RfcCliente, "Cliente Maquila", "601", "97000", "G03", "MEX",
        "BBB010101BBB", "601", "PUE", "03", "MXN", null, TipoAnticipo.ClientesMxp, montoBase, 0.16m, null, null, "AW-7", null, null);

    private static EmitirFacturaVentaCommand FacturaCommand(IReadOnlyList<AnticipoAAmortizar> anticipos) => new(
        SucursalId: Guid.NewGuid(),
        ReceptorRfc: RfcCliente, ReceptorNombre: "Cliente Maquila", ReceptorRegimenFiscal: "601",
        ReceptorCodigoPostal: "97000", ReceptorUsoCfdi: "G03", ReceptorPais: "MEX",
        RfcEmisor: "BBB010101BBB", RegimenFiscalEmisor: "601",
        MetodoPago: "PUE", FormaPago: "03", Moneda: "MXN", TipoCambio: null,
        CanalVenta: (short)7, ComportamientoFiscal: ComportamientoFiscal.ConAnticipo,
        ObraId: null, ObraNombre: null, FacturaAgrupada: false,
        Lineas: [new EmitirFacturaVentaLinea(null, "01010101", "Servicio maquila", "E48", 1m, 10000m, 0m, "02", 0.16m, null, null)],
        Anticipos: anticipos);

    // ---- 13-J: caso VEN-000003 — dos anticipos, ambas relaciones 07 ----

    [Fact]
    public async Task Emitir_con_dos_anticipos_timbrados_relaciona_ambos_y_genera_dos_NCs()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();
        var fiscal = new FakeFiscalUuidsUnicos();
        var a1 = await EmitirAnticipoHandler(db, empresaId, sender, fiscal).Handle(AnticipoCommand(), CancellationToken.None);
        var a2 = await EmitirAnticipoHandler(db, empresaId, sender, fiscal).Handle(AnticipoCommand(), CancellationToken.None);

        var resp = await FacturaHandler(db, empresaId, sender, fiscal).Handle(
            FacturaCommand([new AnticipoAAmortizar(a1.AnticipoId, 300m), new AnticipoAAmortizar(a2.AnticipoId, 400m)]),
            CancellationToken.None);

        resp.Estado.Should().Be("Timbrado");
        resp.NotasCreditoAmortizacion.Should().HaveCount(2);

        var factura = await db.FacturasVenta.Include(f => f.Relaciones).SingleAsync();
        factura.Relaciones.Where(r => r.TipoRelacion == "07").Should().HaveCount(2);

        var uuidsAnticipos = await db.FacturasAnticipo.Select(f => f.Uuid!).ToListAsync();
        factura.Relaciones.Select(r => r.UuidRelacionado).Should().BeEquivalentTo(uuidsAnticipos);
    }

    [Fact]
    public async Task Amortizar_anticipo_con_cfdi_no_timbrado_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();
        var fallido = await EmitirAnticipoHandler(db, empresaId, sender, new FakeFiscalSiempreFalla())
            .Handle(AnticipoCommand(), CancellationToken.None);
        fallido.Estado.Should().Be("TimbradoFallido");

        var act = () => FacturaHandler(db, empresaId, sender, new FakeFiscalUuidsUnicos())
            .Handle(FacturaCommand([new AnticipoAAmortizar(fallido.AnticipoId, 100m)]), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_CFDI_NO_TIMBRADO");
        (await db.FacturasVenta.CountAsync()).Should().Be(0); // no quemó folio de venta
    }

    [Fact]
    public async Task Vincular_anticipo_con_cfdi_no_timbrado_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();
        var fiscal = new FakeFiscalUuidsUnicos();
        var fallido = await EmitirAnticipoHandler(db, empresaId, sender, new FakeFiscalSiempreFalla())
            .Handle(AnticipoCommand(), CancellationToken.None);
        // Una factura timbrada a la cual vincular.
        await FacturaHandler(db, empresaId, sender, fiscal).Handle(FacturaCommand([]), CancellationToken.None);
        var factura = await db.FacturasVenta.SingleAsync();

        var act = () => new VincularAnticipoHandler(db, new FakeClock(Ahora))
            .Handle(new VincularAnticipoCommand(fallido.AnticipoId, factura.Id, 100m), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_CFDI_NO_TIMBRADO");
    }

    // ---- 13-A: detalle enriquecido ----

    [Fact]
    public async Task Detalle_incluye_saldo_vinculaciones_y_nc_de_amortizacion()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();
        var fiscal = new FakeFiscalUuidsUnicos();
        var anticipo = await EmitirAnticipoHandler(db, empresaId, sender, fiscal).Handle(AnticipoCommand(), CancellationToken.None);
        await FacturaHandler(db, empresaId, sender, fiscal)
            .Handle(FacturaCommand([new AnticipoAAmortizar(anticipo.AnticipoId, 500m)]), CancellationToken.None);

        var detalle = await new FacturaAnticipoDetalleHandler(db, new FakeAlcanceCajaEvaluator())
            .Handle(new FacturaAnticipoDetalleQuery(anticipo.FacturaAnticipoId), CancellationToken.None);

        detalle.Anticipo.Should().NotBeNull();
        detalle.Anticipo!.MontoCobrado.Should().Be(1160m);
        detalle.Anticipo.MontoAmortizado.Should().Be(500m);
        detalle.Anticipo.Saldo.Should().Be(660m);
        detalle.Anticipo.Estado.Should().Be("Abierto");

        var vinc = detalle.Anticipo.Vinculaciones.Should().ContainSingle().Subject;
        vinc.Importe.Should().Be(500m);
        vinc.FacturaFolio.Should().NotBeNullOrEmpty();
        vinc.FacturaEstado.Should().Be("Timbrado");
        vinc.NcAmortizacionId.Should().NotBeNull();
        vinc.NcFolio.Should().NotBeNullOrEmpty();
        vinc.NcEstado.Should().Be("Timbrado");
    }

    [Fact]
    public async Task Detalle_de_timbrado_fallido_expone_el_error()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();
        var fallido = await EmitirAnticipoHandler(db, empresaId, sender, new FakeFiscalSiempreFalla())
            .Handle(AnticipoCommand(), CancellationToken.None);

        var detalle = await new FacturaAnticipoDetalleHandler(db, new FakeAlcanceCajaEvaluator())
            .Handle(new FacturaAnticipoDetalleQuery(fallido.FacturaAnticipoId), CancellationToken.None);

        detalle.Estado.Should().Be("TimbradoFallido");
        detalle.TimbradoErrorCodigo.Should().Be("CFDI40100");
        detalle.TimbradoErrorMensaje.Should().Be("Rechazo del PAC");
    }

    // ---- 13-H: bandeja con saldo ----

    [Fact]
    public async Task Bandeja_incluye_estado_y_saldo_del_anticipo()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();
        await EmitirAnticipoHandler(db, empresaId, sender, new FakeFiscalUuidsUnicos())
            .Handle(AnticipoCommand(), CancellationToken.None);

        var bandeja = await new BandejaFacturasAnticipoHandler(db, new FakeAlcanceCajaEvaluator())
            .Handle(new BandejaFacturasAnticipoQuery(null, 0, 50), CancellationToken.None);

        var item = bandeja.Items.Should().ContainSingle().Subject;
        item.EstadoAnticipo.Should().Be("Abierto");
        item.Saldo.Should().Be(1160m);
    }

    // ---- 13-B/13-C: descargas genéricas ----

    [Fact]
    public async Task Pdf_de_anticipo_y_nc_generan_bytes()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();
        var fiscal = new FakeFiscalUuidsUnicos();
        var anticipo = await EmitirAnticipoHandler(db, empresaId, sender, fiscal).Handle(AnticipoCommand(), CancellationToken.None);
        await FacturaHandler(db, empresaId, sender, fiscal)
            .Handle(FacturaCommand([new AnticipoAAmortizar(anticipo.AnticipoId, 500m)]), CancellationToken.None);
        var nc = await db.NotasCredito.SingleAsync();

        var handler = new ComprobantePdfHandler(db, new FakeAlcanceCajaEvaluator(), new QuestPdfFacturaGenerator());

        var pdfAnticipo = await handler.Handle(
            new ComprobantePdfQuery(anticipo.FacturaAnticipoId, FormatoPdfFactura.Bilingue, FamiliaComprobante.FacturaAnticipo),
            CancellationToken.None);
        pdfAnticipo.Contenido.Should().NotBeEmpty();
        pdfAnticipo.NombreSugerido.Should().StartWith("factura-anticipo-");

        var pdfNc = await handler.Handle(
            new ComprobantePdfQuery(nc.Id, FormatoPdfFactura.Bilingue, FamiliaComprobante.NotaCredito),
            CancellationToken.None);
        pdfNc.Contenido.Should().NotBeEmpty();
        pdfNc.NombreSugerido.Should().StartWith("nota-credito-");
    }

    [Fact]
    public async Task Descarga_con_familia_equivocada_es_404()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();
        var fiscal = new FakeFiscalUuidsUnicos();
        await FacturaHandler(db, empresaId, sender, fiscal).Handle(FacturaCommand([]), CancellationToken.None);
        var factura = await db.FacturasVenta.SingleAsync();

        // Una factura de venta NO es descargable vía el grupo de anticipos.
        var act = () => new ComprobanteXmlHandler(db, new FakeAlcanceCajaEvaluator(), new FakeCfdiRepositorioPort())
            .Handle(new ComprobanteXmlQuery(factura.Id, FamiliaComprobante.FacturaAnticipo), CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>().Where(e => e.Code == "COMPROBANTE_NO_ENCONTRADO");
    }
}
