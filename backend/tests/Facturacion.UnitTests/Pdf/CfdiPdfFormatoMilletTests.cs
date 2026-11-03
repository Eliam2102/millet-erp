using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Millet.Facturacion.Application.Comprobantes.Pdf;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Pdf;
using Millet.Facturacion.Infrastructure.Pdf.CfdiMillet;
using Xunit;

namespace Millet.Facturacion.UnitTests.Pdf;

/// <summary>
/// FMT-4 (86e3ukucq): PDF de factura, anticipo y nota de crédito con el formato
/// de Millet. Armado del modelo campo por campo y plantilla con Carlito.
/// Solo datos ficticios (RFC de pruebas del SAT, UUID y sellos inventados).
/// </summary>
public sealed class CfdiPdfFormatoMilletTests
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly Guid Sucursal = Guid.Parse("00000005-0003-0000-0000-000000000001");
    private static readonly DatosFiscalesEmisor Emisor = new("EKU9003173C9", "VIDRIOS DEMO INDUSTRIALES SA DE CV", "601", "97000");
    private static readonly DatosFiscalesReceptor Receptor =
        new("XAXX010101000", "COMERCIAL DEMO DEL SURESTE SA DE CV", "601", "97000", "G03", "MEX", false);
    private static readonly string Sello = string.Concat(Enumerable.Repeat("AAAA", 86));
    private static readonly DateTimeOffset Fecha = new(2026, 10, 22, 10, 30, 0, TimeSpan.FromHours(-6));

    private static readonly DatosImpresionCfdi Datos = new(
        "DEMO",
        ["Calle Ficticia 100", "Col. Centro, Mérida", "Yucatán, México, CP 97000"],
        null,
        new Dictionary<string, string> { ["601"] = "General de Ley Personas Morales" },
        new Dictionary<string, string> { ["G03"] = "Gastos en general" },
        new Dictionary<string, string> { ["03"] = "Transferencia electrónica de fondos" });

    [Fact]
    public void Factura_arma_el_modelo_con_datos_del_cliente_medidas_comentarios_y_timbre()
    {
        var f = Factura(lineas: 2, timbrar: true);

        var m = CfdiPdfModelBuilder.DesdeFactura(f, Datos);

        m.Tipo.Should().Be(TipoDocumentoPdf.Factura);
        (m.TituloEs, m.TituloEn, m.Serie, m.Folio).Should().Be(("FACTURA", "INVOICE", "DEMOF", "000101"));
        m.TipoComprobante.Should().Be("Ingreso");
        m.Emisor.DomicilioLineas.Should().HaveCount(3);
        m.Emisor.Regimen.Should().Be("601 - General de Ley Personas Morales");
        m.Receptor.CondicionesPago.Should().Be("30 DÍAS");
        m.Cabecera.OrdenCompraCliente.Should().Be("OC-DEMO-77");
        m.Cabecera.Referencia.Should().Be("OBRA DEMO");
        m.Cabecera.NoCertificadoEmisor.Should().Be("30001000000500003416");
        m.Conceptos.Select(c => c.LineaReferencia).Should().Equal("ITEM.1 REF: PED-DEMO-1", "ITEM.2 REF: PED-DEMO-2");
        m.TotalesFisicos.Should().Be(new TotalesFisicosPdf(25.5m, 17.25m, 2));
        m.Comentarios.Should().Be("PROFORMA: PF-DEMO-1\nEntregar en obra");
        m.Impuestos.Should().ContainSingle().Which.Should().Be(new ImpuestoResumenPdf("002 IVA", "Tasa", 0.16m, 2000m, 320m, false));
        m.Pago.Should().Be(new PagoPdf("G03 - Gastos en general", "PUE - Pago en una sola exhibición", "03 - Transferencia electrónica de fondos"));
        m.MontoEnLetra.Es.Should().Be("DOS MIL TRESCIENTOS VEINTE PESOS 00/100 M.N.");
        m.MontoEnLetra.En.Should().Be("TWO THOUSAND THREE HUNDRED TWENTY PESOS 00/100 M.N.");
        m.Timbre!.CadenaOriginal.Should().StartWith("||1.1|00000000-0000-4000-8000-000000000101|2026-10-22T10:30:00|SPR190613I52|");
        m.Timbre.UrlQr.Should().Contain("id=00000000-0000-4000-8000-000000000101").And.Contain("tt=0000002320.000000").And.EndWith("fe=AAAAAAAA");
        m.Relacionados.Should().Equal(new CfdiRelacionadoPdf("00000000-0000-4000-8000-000000000201", "07 - CFDI por aplicación de anticipo"));
        m.NombreArchivo.Should().Be("factura-DEMOF-000101-bilingue.pdf");
    }

    [Fact]
    public void Anticipo_imprime_la_unidad_ACT_y_el_IVA_del_concepto_unico()
    {
        var m = CfdiPdfModelBuilder.DesdeAnticipo(Anticipo(), Datos);

        // El original FANT 504596 se titula igual que la factura; la serie distingue el anticipo.
        new CfdiMilletDocument(m).Titulo.Should().Be("FACTURA/INVOICE:DEMOA 000201");
        var concepto = m.Conceptos.Should().ContainSingle().Subject;
        (concepto.ClaveProdServ, concepto.ClaveUnidad, concepto.Cantidad, concepto.Importe).Should().Be(("84111506", "ACT", 1m, 1000m));
        m.Impuestos.Should().ContainSingle().Which.TasaOCuota.Should().Be(0.16m);
        m.Totales.Total.Should().Be(1160m);
        m.TotalesFisicos.Should().Be(new TotalesFisicosPdf(null, null, 1));
        m.MontoEnLetra.Es.Should().Be("MIL CIENTO SESENTA PESOS 00/100 M.N.");
    }

    [Fact]
    public void Nota_de_credito_por_aplicacion_de_anticipo_es_Egreso_con_relacionados_07()
    {
        var nc = NotaCreditoAmortizacion();

        var m = CfdiPdfModelBuilder.DesdeNotaCredito(nc, Datos);

        m.TipoComprobante.Should().Be("Egreso");
        new CfdiMilletDocument(m).Titulo.Should().Be("NOTA DE CRÉDITO/CREDIT MEMO:DEMON 000301");
        m.Conceptos.Should().ContainSingle().Which.ClaveUnidad.Should().Be("ACT");
        m.Relacionados.Should().Equal(
            new CfdiRelacionadoPdf("00000000-0000-4000-8000-000000000201", "07 - CFDI por aplicación de anticipo"),
            new CfdiRelacionadoPdf("00000000-0000-4000-8000-000000000101", "07 - CFDI por aplicación de anticipo"));
        m.TotalesFisicos.Should().Be(new TotalesFisicosPdf(0, 0, 1));
        m.Impuestos.Should().ContainSingle().Which.Nombre.Should().Be("002 IVA");
        m.NombreArchivo.Should().Be("nota-credito-DEMON-000301.pdf");
    }

    [Fact]
    public void Sin_timbre_no_hay_bloque_de_sellos_y_sin_catalogos_imprime_solo_claves()
    {
        var m = CfdiPdfModelBuilder.DesdeFactura(Factura(lineas: 1, timbrar: false), null);

        m.Timbre.Should().BeNull();
        m.Emisor.Regimen.Should().Be("601");
        m.Pago.FormaPago.Should().Be("03");
        m.Emisor.DomicilioLineas.Should().BeEmpty();
    }

    [Fact]
    public void La_plantilla_incrusta_Carlito_y_no_pide_Calibri()
    {
        var gen = new QuestPdfFacturaGenerator();

        foreach (var pdf in new[]
                 {
                     gen.Generar(CfdiPdfModelBuilder.DesdeFactura(Factura(lineas: 3, timbrar: true), Datos)),
                     gen.Generar(CfdiPdfModelBuilder.DesdeAnticipo(Anticipo(), Datos)),
                     gen.Generar(CfdiPdfModelBuilder.DesdeNotaCredito(NotaCreditoAmortizacion(), Datos)),
                 })
        {
            var texto = Encoding.Latin1.GetString(pdf.Contenido);
            texto.Should().StartWith("%PDF");
            texto.Should().Contain("Carlito").And.NotContain("Calibri");
            pdf.ContentType.Should().Be("application/pdf");
        }
    }

    [Fact]
    public void Una_factura_larga_agrega_paginas_y_el_timbre_no_se_parte()
    {
        var gen = new QuestPdfFacturaGenerator();

        // El bloque del timbre (QR, sellos y cadena) va completo: si no cabe pasa a la página siguiente.
        var corta = Paginas(gen.Generar(CfdiPdfModelBuilder.DesdeFactura(Factura(lineas: 2, timbrar: true), Datos)).Contenido);
        var larga = Paginas(gen.Generar(CfdiPdfModelBuilder.DesdeFactura(Factura(lineas: 40, timbrar: true), Datos)).Contenido);

        corta.Should().BeInRange(1, 2);
        larga.Should().BeGreaterThan(corta);
    }

    [Fact]
    public void Con_sello_corto_del_stub_imprime_los_sellos_sin_QR()
    {
        var f = Factura(lineas: 1, timbrar: false);
        f.MarcarTimbradoEnProceso();
        f.MarcarTimbrado("00000000-0000-4000-8000-000000000199", "STUB", "STUB", null, Fecha, null, null);

        var m = CfdiPdfModelBuilder.DesdeFactura(f, Datos);

        m.Timbre!.UrlQr.Should().BeNull();
        new QuestPdfFacturaGenerator().Generar(m).Contenido.Should().NotBeEmpty();
    }

    [Fact]
    public void El_ticket_termico_sigue_saliendo_con_Carlito()
    {
        var pdf = new QuestPdfFacturaGenerator().Generar(Factura(lineas: 2, timbrar: true), FormatoPdfFactura.TermicaSimplificada);

        Encoding.Latin1.GetString(pdf.Contenido).Should().Contain("Carlito").And.NotContain("Calibri");
        pdf.NombreSugerido.Should().EndWith("-termica.pdf");
    }

    [Theory]
    [InlineData("1065.9", "1,065.90")]
    [InlineData("15295.755", "15,295.76")]
    [InlineData("25", "25.00")]
    public void Kg_y_pie2_se_imprimen_con_dos_decimales(string valor, string esperado) =>
        CfdiMilletDocument.Medida(decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(esperado);

    [Fact]
    public void Sin_OC_referencia_ni_condiciones_el_PDF_conserva_los_rotulos()
    {
        var f = FacturaVenta.CrearBorrador(Empresa, "DEMOF-000102", 102, Sucursal, null, null, Receptor, Emisor,
            "PUE", "03", "MXN", null, 2026, 10, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(null, "30171700", "VIDRIO DEMO", "H87", 1, 1000, 0, "02", 0.16m, null, null);
        f.RecalcularTotales();
        Timbrar(f, "00000000-0000-4000-8000-000000000102");

        var m = CfdiPdfModelBuilder.DesdeFactura(f, Datos);

        // Recuadro CFDI del original: siempre los siete renglones, aunque OC y referencia vayan vacíos.
        CfdiMilletDocument.FilasCfdi(m).Select(r => r.Etiqueta).Should().Equal(
            "N° Orden de compra/Purchase order:", "N° Referencia/Reference", "Certificado CSD/CSD Certificate:",
            "Certificado del SAT/Sat Certificate Serial Number:", "Fecha de Timbrado/Certification date:",
            "Folio Fiscal/Invoice id (uuid):", "Tipo Comprobante:");
        var filas = CfdiMilletDocument.FilasCfdi(m);
        (filas[0].Valor, filas[1].Valor, filas[^1].Valor).Should().Be((null, null, "Ingreso"));
        new QuestPdfFacturaGenerator().Generar(m).Contenido.Should().NotBeEmpty();
    }

    [Fact]
    public void El_codigo_lleva_el_articulo_y_la_clave_SAT_cuando_la_linea_tiene_articulo()
    {
        var producto = Guid.NewGuid();
        var f = FacturaVenta.CrearBorrador(Empresa, "DEMOF-000103", 103, Sucursal, null, null, Receptor, Emisor,
            "PUE", "03", "MXN", null, 2026, 10, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(producto, "30171710", "VIDRIO INSULADO DEMO", "H87", 3, 100, 0, "02", 0.16m, null, null);
        f.AgregarLinea(null, "30171700", "VIDRIO SIN ARTICULO", "H87", 1, 100, 0, "02", 0.16m, null, null);
        f.RecalcularTotales();

        var m = CfdiPdfModelBuilder.DesdeFactura(f, Datos, new Dictionary<Guid, string> { [producto] = "VIDB0002" });

        m.Conceptos.Select(CfdiMilletDocument.Codigo).Should().Equal("VIDB0002 / 30171710", "30171700");
    }

    private static int Paginas(byte[] pdf) =>
        Regex.Count(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page(?!s)");

    private static FacturaVenta Factura(int lineas, bool timbrar)
    {
        var f = FacturaVenta.CrearBorrador(Empresa, "DEMOF-000101", 101, Sucursal, null, null, Receptor, Emisor,
            "PUE", "03", "MXN", null, 2026, 10, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        for (var i = 1; i <= lineas; i++)
            f.AgregarLinea(null, "30171700", $"VIDRIO TEMPLADO DEMO {i}", "H87", 1, 1000, 0, "02", 0.16m, null, null,
                referencia: $"PED-DEMO-{i}", pesoKg: i == 1 ? 25.5m : null, areaPie2: i == 1 ? 17.25m : null);
        f.EditarDatosCliente("OC-DEMO-77", "OBRA DEMO", "30 DÍAS", "PF-DEMO-1", "Entregar en obra");
        f.RecalcularTotales();
        f.AgregarRelacion("00000000-0000-4000-8000-000000000201", "07");
        if (timbrar) Timbrar(f, "00000000-0000-4000-8000-000000000101");
        return f;
    }

    private static FacturaAnticipo Anticipo()
    {
        var a = FacturaAnticipo.CrearBorrador(Empresa, "DEMOA-000201", 201, Sucursal, null, null, Receptor, Emisor,
            "03", "MXN", null, 2026, 10, TipoAnticipo.ClientesMxp, null, Guid.NewGuid(), 1000, 0.16m, "ANTICIPO DE CLIENTES");
        Timbrar(a, "00000000-0000-4000-8000-000000000201");
        return a;
    }

    private static NotaCredito NotaCreditoAmortizacion()
    {
        var nc = NotaCredito.CrearAmortizacion(Empresa, "DEMON-000301", 301, Sucursal, null, null, Receptor, Emisor,
            "03", "MXN", null, 2026, 10, null, Guid.NewGuid(), Guid.NewGuid(), 1160, 0.16m);
        nc.AgregarRelacion("00000000-0000-4000-8000-000000000201", "07");
        nc.AgregarRelacion("00000000-0000-4000-8000-000000000101", "07");
        Timbrar(nc, "00000000-0000-4000-8000-000000000301");
        return nc;
    }

    private static void Timbrar(Comprobante c, string uuid)
    {
        c.MarcarTimbradoEnProceso();
        c.MarcarTimbrado(uuid, Sello, Sello, "30001000000500003456", Fecha, "SPR190613I52", null);
        c.RecalcularDatosTimbre($"""
            <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" NoCertificado="30001000000500003416">
              <cfdi:Complemento>
                <tfd:TimbreFiscalDigital xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="1.1" UUID="{uuid}"
                  FechaTimbrado="2026-10-22T10:30:00" RfcProvCertif="SPR190613I52" SelloCFD="{Sello}" NoCertificadoSAT="30001000000500003456" />
              </cfdi:Complemento>
            </cfdi:Comprobante>
            """);
    }
}
