using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Millet.Facturacion.Application.Comprobantes.Pdf;
using Millet.Facturacion.Domain.Cce;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Pdf;
using UglyToad.PdfPig;
using Xunit;

namespace Millet.Facturacion.UnitTests.Pdf;

/// <summary>
/// Documentos fiscales impresos, parte A (86e3tfdx0): PDF del complemento de pago
/// con el diseño de la factura de Millet, bloque de comercio exterior en la factura
/// de exportación y los cortes de página de FMT-4 (timbre entero, «ITEM.n REF» con
/// su concepto). Solo datos ficticios (RFC de pruebas del SAT, UUID y sellos inventados).
/// </summary>
public sealed partial class DocumentosFiscalesImpresosTests
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly Guid Sucursal = Guid.Parse("00000005-0003-0000-0000-000000000001");
    private static readonly DatosFiscalesEmisor Emisor = new("EKU9003173C9", "VIDRIOS DEMO INDUSTRIALES SA DE CV", "601", "97000");
    private static readonly DatosFiscalesReceptor Receptor =
        new("XAXX010101000", "COMERCIAL DEMO DEL SURESTE SA DE CV", "601", "97000", "G03", "MEX", false);
    private static readonly DatosFiscalesReceptor ReceptorPago =
        new("XAXX010101000", "COMERCIAL DEMO DEL SURESTE SA DE CV", "601", "97000", "CP01", "MEX", false);
    private static readonly DatosFiscalesReceptor ReceptorExtranjero =
        new("XEXX010101000", "DEMO GLASS IMPORTS LLC", "616", "97000", "S01", "USA", false);
    private static readonly string Sello = string.Concat(Enumerable.Repeat("AAAA", 86));
    private static readonly DateTimeOffset Fecha = new(2026, 10, 22, 10, 30, 0, TimeSpan.FromHours(-6));
    private static readonly Guid FacturaA = Guid.Parse("00000000-0000-4000-8000-00000000a001");
    private static readonly Guid FacturaB = Guid.Parse("00000000-0000-4000-8000-00000000a002");

    private static readonly DatosImpresionCfdi Datos = new(
        "DEMO",
        ["Calle Ficticia 100", "Col. Centro, Mérida", "Yucatán, México, CP 97000"],
        null,
        new Dictionary<string, string> { ["601"] = "General de Ley Personas Morales" },
        new Dictionary<string, string> { ["CP01"] = "Pagos" },
        new Dictionary<string, string> { ["03"] = "Transferencia electrónica de fondos" });

    // ---- Criterio 1: complemento de pago ----

    [Fact]
    public void Complemento_de_pago_arma_el_modelo_con_las_dos_facturas_parcialidad_y_saldos()
    {
        var m = CfdiPdfModelBuilder.DesdeReciboPago(NuevoReciboPago(), Datos, Folios);

        m.Tipo.Should().Be(TipoDocumentoPdf.ComplementoPago);
        (m.TituloEs, m.TituloEn, m.Serie, m.Folio).Should().Be(("COMPLEMENTO DE PAGO", "PAYMENT RECEIPT", "DEMOP", "000401"));
        m.TipoComprobante.Should().Be("Pago");
        m.Conceptos.Should().ContainSingle().Which.Should().Be(new ConceptoPdf(null, "84111506", "Pago", null, 1, "ACT", 0, 0, 0));
        m.Impuestos.Should().BeEmpty();
        m.Pago.UsoCfdi.Should().Be("CP01 - Pagos");
        m.MontoEnLetra.Es.Should().Be("CINCO MIL PESOS 00/100 M.N.");

        var pago = m.ComplementoPago!;
        pago.FechaPago.Should().Be(Fecha.ToUniversalTime());
        (pago.FormaPago, pago.Moneda, pago.Monto).Should().Be(("03 - Transferencia electrónica de fondos", "MXN", 5000m));
        (pago.NumeroOperacion, pago.CuentaOrdenante, pago.CuentaBeneficiaria).Should().Be(("SPEI-DEMO-1", "0123456789", "9876543210"));
        pago.Documentos.Should().Equal(
            new DocumentoPagadoPdf("00000000-0000-4000-8000-000000000111", "DEMOF-000111", "MXN", 1, 3480m, 3000m, 480m),
            new DocumentoPagadoPdf("00000000-0000-4000-8000-000000000112", "DEMOF-000112", "MXN", 2, 2320m, 2000m, 320m));

        m.Timbre!.UrlQr.Should().Contain("id=00000000-0000-4000-8000-000000000401").And.Contain("tt=0000000000.000000");
        m.NombreArchivo.Should().Be("complemento-pago-DEMOP-000401.pdf");
        m.ComercioExterior.Should().BeNull();
    }

    [Fact]
    public void Complemento_de_pago_imprime_datos_del_pago_documentos_relacionados_sellos_y_QR()
    {
        var pdf = new QuestPdfFacturaGenerator().Generar(CfdiPdfModelBuilder.DesdeReciboPago(NuevoReciboPago(), Datos, Folios));

        using var doc = PdfDocument.Open(pdf.Contenido);
        var texto = SinEspacios(string.Concat(doc.GetPages().Select(p => p.Text)));
        texto.Should().Contain("COMPLEMENTODEPAGO/PAYMENTRECEIPT:DEMOP000401");
        texto.Should().Contain("DATOSDELPAGO/PAYMENTDATA");
        texto.Should().Contain("03-Transferenciaelectrónicadefondos");
        texto.Should().Contain("SPEI-DEMO-1");
        texto.Should().Contain("DOCUMENTOSRELACIONADOS/RELATEDDOCUMENTS");
        texto.Should().Contain("00000000-0000-4000-8000-000000000111").And.Contain("00000000-0000-4000-8000-000000000112");
        texto.Should().Contain("DEMOF-000111").And.Contain("DEMOF-000112");
        texto.Should().Contain("3,480.00").And.Contain("3,000.00").And.Contain("480.00");
        texto.Should().Contain("2,320.00").And.Contain("2,000.00").And.Contain("320.00");
        texto.Should().Contain("5,000.00MXN");
        texto.Should().Contain("CINCOMILPESOS00/100M.N.");
        texto.Should().Contain("SelloDigitaldelCFDI").And.Contain("SelloDigitaldelSAT");
        // El CFDI de pago no lleva totales, impuestos ni medidas de la factura.
        texto.Should().NotContain("TotalKg.").And.NotContain("TipoImpuesto");
        doc.GetPages().SelectMany(p => p.GetImages()).Should().NotBeEmpty("el QR va como imagen");
        pdf.NombreSugerido.Should().Be("complemento-pago-DEMOP-000401.pdf");
    }

    // ---- Criterio 2: comercio exterior ----

    [Fact]
    public void Factura_de_exportacion_lleva_incoterm_tipo_de_cambio_total_en_dolares_y_registro_tributario()
    {
        var m = CfdiPdfModelBuilder.DesdeFactura(FacturaExportacion(), Datos);

        m.ComercioExterior.Should().Be(new ComercioExteriorPdf("2", "FOB", "A1", false, 18.5432m, 1500.25m, "DEMO-TAX-123", "USA"));

        var pdf = new QuestPdfFacturaGenerator().Generar(m);
        using var doc = PdfDocument.Open(pdf.Contenido);
        var texto = SinEspacios(string.Concat(doc.GetPages().Select(p => p.Text)));
        texto.Should().Contain("COMERCIOEXTERIOR/FOREIGNTRADE");
        texto.Should().Contain("Incoterm:FOB");
        texto.Should().Contain("18.543200");
        texto.Should().Contain("1,500.25USD");
        texto.Should().Contain("DEMO-TAX-123");
    }

    [Fact]
    public void Factura_nacional_no_lleva_bloque_de_comercio_exterior()
    {
        var m = CfdiPdfModelBuilder.DesdeFactura(Factura(lineas: 2), Datos);

        m.ComercioExterior.Should().BeNull();
        using var doc = PdfDocument.Open(new QuestPdfFacturaGenerator().Generar(m).Contenido);
        SinEspacios(string.Concat(doc.GetPages().Select(p => p.Text))).Should().NotContain("COMERCIOEXTERIOR");
    }

    // ---- Criterio 3: cortes de página de FMT-4 ----

    [Theory]
    [InlineData(40, false)]
    [InlineData(40, true)]
    [InlineData(30, true)]
    [InlineData(34, true)]
    [InlineData(37, true)]
    [InlineData(43, true)]
    [InlineData(46, false)]
    public void Cada_ITEM_REF_queda_en_la_pagina_de_su_concepto(int lineas, bool descripcionLarga)
    {
        using var doc = PdfDocument.Open(new QuestPdfFacturaGenerator()
            .Generar(CfdiPdfModelBuilder.DesdeFactura(Factura(lineas, descripcionLarga), Datos)).Contenido);

        var conceptos = new Dictionary<int, int>();
        var referencias = new Dictionary<int, int>();
        foreach (var pagina in doc.GetPages())
        {
            var texto = SinEspacios(pagina.Text);
            foreach (Match x in Concepto().Matches(texto)) conceptos[int.Parse(x.Groups[1].Value, CultureInfo.InvariantCulture)] = pagina.Number;
            foreach (Match x in Referencia().Matches(texto)) referencias[int.Parse(x.Groups[1].Value, CultureInfo.InvariantCulture)] = pagina.Number;
        }

        conceptos.Keys.Should().BeEquivalentTo(Enumerable.Range(1, lineas));
        referencias.Should().BeEquivalentTo(conceptos);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(25)]
    public void El_timbre_queda_completo_en_una_pagina(int lineas)
    {
        using var doc = PdfDocument.Open(new QuestPdfFacturaGenerator()
            .Generar(CfdiPdfModelBuilder.DesdeFactura(Factura(lineas), Datos)).Contenido);

        var piezas = new[] { "SelloDigitaldelCFDI", "SelloDigitaldelSAT", "CadenaOriginaldelComplemento", "FechadeTimbrado:" };
        var paginas = piezas.Select(p => doc.GetPages().Where(pg => SinEspacios(pg.Text).Contains(p, StringComparison.Ordinal))
            .Select(pg => pg.Number).ToList()).ToList();

        paginas.Should().AllSatisfy(p => p.Should().ContainSingle());
        paginas.Select(p => p[0]).Distinct().Should().ContainSingle("sellos, cadena, QR y fecha van juntos");
    }

    private static readonly IReadOnlyDictionary<Guid, string> Folios = new Dictionary<Guid, string>
    {
        [FacturaA] = "DEMOF-000111",
        [FacturaB] = "DEMOF-000112",
    };

    private static ReciboPago NuevoReciboPago()
    {
        var r = ReciboPago.CrearBorrador(Empresa, "DEMOP-000401", 401, Sucursal, null, null, ReceptorPago, Emisor,
            2026, 10, Fecha, "MXN");
        // Se agregan en desorden: el PDF las ordena por parcialidad.
        r.AgregarFacturaPagada(FacturaB, "00000000-0000-4000-8000-000000000112", 2, "MXN", 2000m, 2320m, "03",
            null, null, null, null, null, "01", 2320m, []);
        r.AgregarFacturaPagada(FacturaA, "00000000-0000-4000-8000-000000000111", 1, "MXN", 3000m, 3480m, "03",
            null, null, "0123456789", "9876543210", "SPEI-DEMO-1", "01", 3480m, []);
        r.EstablecerImporteTotalPago(5000m);
        Timbrar(r, "00000000-0000-4000-8000-000000000401");
        return r;
    }

    private static FacturaVenta FacturaExportacion()
    {
        var f = FacturaVenta.CrearBorrador(Empresa, "DEMOE-000501", 501, Sucursal, null, null, ReceptorExtranjero, Emisor,
            "PPD", "99", "USD", 18.5432m, 2026, 10, 1, ComportamientoFiscal.ExportacionConCce, null, null, null, false);
        f.AgregarLinea(null, "30171710", "INSULATED GLASS DEMO", "H87", 2, 500, 0, "02", 0m, null, null);
        f.AgregarLinea(null, "30171710", "TEMPERED GLASS DEMO", "H87", 1, 500.25m, 0, "02", 0m, null, null);
        f.RecalcularTotales();
        var cce = ComplementoCce.Crear(f.Id, "2", "FOB", 18.5432m, "DEMO-TAX-123", "usa", claveDePedimento: "A1");
        cce.AgregarLinea("7008000101", "06", 2, 500, 1000m, true);
        cce.AgregarLinea("7007190101", "06", 1, 500.25m, 500.25m, true);
        f.AdjuntarCce(cce);
        Timbrar(f, "00000000-0000-4000-8000-000000000501");
        return f;
    }

    /// <param name="descripcionLarga">Como en FEXP: la descripción ocupa dos renglones de la columna.</param>
    private static FacturaVenta Factura(int lineas, bool descripcionLarga = false)
    {
        var f = FacturaVenta.CrearBorrador(Empresa, "DEMOF-000101", 101, Sucursal, null, null, Receptor, Emisor,
            "PUE", "03", "MXN", null, 2026, 10, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        for (var i = 1; i <= lineas; i++)
            f.AgregarLinea(null, "30171700", descripcionLarga
                    ? $"INSULATED GLASS / VIDRIO INSULADO DEMO 78\" 1/2 X 41\" 3/4 DEMO D{i:000}"
                    : $"VIDRIO TEMPLADO DEMO D{i:000}", "H87", 1, 1000, 0, "02", 0.16m, null, null,
                referencia: $"R{i:000}");
        f.RecalcularTotales();
        Timbrar(f, "00000000-0000-4000-8000-000000000101");
        return f;
    }

    private static void Timbrar(Comprobante c, string uuid)
    {
        c.MarcarTimbradoEnProceso();
        c.MarcarTimbrado(uuid, Sello, Sello, "30001000000500003456", Fecha, "SPR190613I52", null);
    }

    private static string SinEspacios(string texto) => Espacios().Replace(texto, "");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacios();

    [GeneratedRegex(@"DEMOD(\d{3})")]
    private static partial Regex Concepto();

    [GeneratedRegex(@"REF:R(\d{3})")]
    private static partial Regex Referencia();
}
