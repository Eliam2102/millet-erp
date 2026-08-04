using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Comprobantes.Queries;
using Millet.Facturacion.Application.Facturas.Queries;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Envios;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Pdf;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Facturas;

/// <summary>B4/B6/B7/B8 — detalle CFDI enriquecido (FE-F2).</summary>
public sealed class DetalleCfdiTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private sealed class FakeCfdiRepoConArchivo(string xml) : ICfdiRepositorioPort
    {
        public Task<Guid> GuardarAsync(CfdiArchivoNuevo a, CancellationToken ct) => Task.FromResult(Guid.NewGuid());
        public Task<CfdiArchivoLeido?> ObtenerAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<CfdiArchivoLeido?>(new CfdiArchivoLeido(id, "U-1", xml, null, "sello", Ahora));
        public Task<CfdiArchivoLeido?> ObtenerPorUuidAsync(string uuid, CancellationToken ct) => Task.FromResult<CfdiArchivoLeido?>(null);
    }

    private static FacturaVenta FacturaTimbrada(Guid empresaId, string uuid, Guid? cfdiArchivoId = null)
    {
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(empresaId, "F-" + uuid[..4], 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "01", "MXN", null, 2026, 5,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, 100m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado(uuid, null, null, null, Ahora, null, cfdiArchivoId);
        return fv;
    }

    // ---- B4: relaciones enriquecidas ----

    [Fact]
    public async Task Detalle_incluye_relaciones_enriquecidas()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var anticipo = FacturaTimbrada(empresaId, "11111111-1111-1111-1111-111111111111");
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
        var final = FacturaVenta.CrearBorrador(empresaId, "F-FIN", 2, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "01", "MXN", null, 2026, 5,
            (short)1, ComportamientoFiscal.ConAnticipo, null, null, null, false);
        final.AgregarLinea(null, "01010101", "P", "H87", 1m, 5000m, 0m, "02", 0.16m, null, null);
        final.RecalcularTotales();
        final.AgregarRelacion(anticipo.Uuid!, "07");
        final.MarcarTimbradoEnProceso();
        final.MarcarTimbrado("22222222-2222-2222-2222-222222222222", null, null, null, Ahora, null, null);
        db.FacturasVenta.AddRange(anticipo, final);
        await db.SaveChangesAsync();

        var detalle = await new ComprobanteDetalleHandler(db, new FakeAlcanceCajaEvaluator())
            .Handle(new ComprobanteDetalleQuery(final.Id), CancellationToken.None);

        detalle.Relaciones.Should().ContainSingle();
        var rel = detalle.Relaciones[0];
        rel.TipoRelacion.Should().Be("07");
        rel.UuidRelacionado.Should().Be(anticipo.Uuid);
        rel.Folio.Should().Be(anticipo.Folio); // enriquecida por UUID
        rel.Total.Should().Be(116m);
    }

    // ---- [Decisión 13-K]: NC aplicadas + monto por cobrar ----

    [Fact]
    public async Task Detalle_incluye_nc_aplicadas_y_total_por_cobrar()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var factura = FacturaTimbrada(empresaId, "33333333-3333-3333-3333-333333333333"); // total 116
        var nc = NotaCredito.CrearAmortizacion(empresaId, "NC-7", 7, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"),
            "01", "MXN", null, 2026, 5, 1, Guid.NewGuid(), factura.Id, 16m, null);
        nc.MarcarTimbradoEnProceso();
        nc.MarcarTimbrado("44444444-4444-4444-4444-444444444444", null, null, null, Ahora, null, null);
        db.FacturasVenta.Add(factura);
        db.NotasCredito.Add(nc);
        await db.SaveChangesAsync();

        var detalle = await new ComprobanteDetalleHandler(db, new FakeAlcanceCajaEvaluator())
            .Handle(new ComprobanteDetalleQuery(factura.Id), CancellationToken.None);

        detalle.TotalAcreditado.Should().Be(16m);
        detalle.TotalPorCobrar.Should().Be(100m);
        var aplicada = detalle.NotasCreditoAplicadas.Should().ContainSingle().Subject;
        aplicada.Folio.Should().Be("NC-7");
        aplicada.Motivo.Should().Be("Amortizacion");
        aplicada.Total.Should().Be(16m);
    }

    // ---- B6: envíos ----

    [Fact]
    public async Task Envios_lista_la_bitacora_de_la_factura()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var fv = FacturaTimbrada(empresaId, "33333333-3333-3333-3333-333333333333");
        db.FacturasVenta.Add(fv);
        var bitacora = BitacoraEnvioCorreo.Encolar(empresaId, fv.Id, "cliente@correo.com");
        bitacora.MarcarEnviado(Ahora);
        db.BitacorasEnvioCorreo.Add(bitacora);
        await db.SaveChangesAsync();

        var items = await new EnviosFacturaHandler(db).Handle(new EnviosFacturaQuery(fv.Id), CancellationToken.None);

        items.Should().ContainSingle();
        items[0].Destinatario.Should().Be("cliente@correo.com");
        items[0].Estado.Should().Be("Enviado");
    }

    // ---- B7: XML ----

    [Fact]
    public async Task Xml_devuelve_el_contenido_del_archivo()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var fv = FacturaTimbrada(empresaId, "44444444-4444-4444-4444-444444444444", cfdiArchivoId: Guid.NewGuid());
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();

        var r = await new ComprobanteXmlHandler(db, new FakeAlcanceCajaEvaluator(), new FakeCfdiRepoConArchivo("<cfdi/>"))
            .Handle(new ComprobanteXmlQuery(fv.Id, FamiliaComprobante.FacturaVenta), CancellationToken.None);

        r.Xml.Should().Be("<cfdi/>");
        r.NombreArchivo.Should().EndWith(".xml");
    }

    [Fact]
    public async Task Xml_de_factura_sin_timbre_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(empresaId, "F-1", 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "01", "MXN", null, 2026, 5,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, 100m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();

        var act = () => new ComprobanteXmlHandler(db, new FakeAlcanceCajaEvaluator(), new FakeCfdiRepoConArchivo("<cfdi/>"))
            .Handle(new ComprobanteXmlQuery(fv.Id, FamiliaComprobante.FacturaVenta), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "COMPROBANTE_SIN_XML");
    }

    // ---- B8: PDF ----

    [Fact]
    public async Task Pdf_genera_bytes_no_vacios()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var fv = FacturaTimbrada(empresaId, "55555555-5555-5555-5555-555555555555");
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();

        var pdf = await new ComprobantePdfHandler(db, new FakeAlcanceCajaEvaluator(), new QuestPdfFacturaGenerator())
            .Handle(new ComprobantePdfQuery(fv.Id, FormatoPdfFactura.Bilingue, FamiliaComprobante.FacturaVenta), CancellationToken.None);

        pdf.Contenido.Should().NotBeEmpty();
        pdf.ContentType.Should().Be("application/pdf");
    }
}
