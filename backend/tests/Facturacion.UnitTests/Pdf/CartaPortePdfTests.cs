using System.Text;
using FluentAssertions;
using Millet.Facturacion.Application.Comprobantes.Pdf;
using Millet.Facturacion.Domain.CartaPorte;
using CartaPorteCfdi = Millet.Facturacion.Domain.CartaPorte.CartaPorte;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Pdf;
using Millet.Facturacion.Infrastructure.Pdf.CfdiMillet;
using Xunit;

namespace Millet.Facturacion.UnitTests.Pdf;

/// <summary>
/// 86e3tfdx0 (criterio 4): PDF de la Carta Porte con el diseño de la factura de Millet:
/// origen, destino, mercancías, vehículo, operador, timbre y QR. Solo datos ficticios.
/// </summary>
public sealed class CartaPortePdfTests
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly Guid Sucursal = Guid.Parse("00000005-0003-0000-0000-000000000001");
    private static readonly DatosFiscalesEmisor Emisor = new("EKU9003173C9", "VIDRIOS DEMO INDUSTRIALES SA DE CV", "601", "97000");
    private static readonly DatosFiscalesReceptor Receptor =
        new("EKU9003173C9", "VIDRIOS DEMO INDUSTRIALES SA DE CV", "601", "97000", "S01", "MEX", false);
    private static readonly string Sello = string.Concat(Enumerable.Repeat("AAAA", 86));
    private static readonly DateTimeOffset Fecha = new(2026, 10, 22, 10, 30, 0, TimeSpan.FromHours(-6));

    private static readonly DatosImpresionCfdi Datos = new(
        "DEMO",
        ["Calle Ficticia 100", "Col. Centro, Mérida", "Yucatán, México, CP 97000"],
        null,
        new Dictionary<string, string> { ["601"] = "General de Ley Personas Morales" },
        new Dictionary<string, string> { ["S01"] = "Sin efectos fiscales" },
        new Dictionary<string, string>());

    private static readonly Vehiculo Camion = Vehiculo.Crear(Empresa, "abc123d", "C2", 2022, "TPAF01", "SCT-DEMO-77",
        "SEGUROS DEMO", "POL-DEMO-1", 17.5m);

    private static readonly Operador Chofer = Operador.Crear(Empresa, "XAXX010101000", "OPERADOR DEMO", "LIC-DEMO-9");

    [Fact]
    public void Traslado_arma_origen_destino_mercancias_vehiculo_operador_y_timbre()
    {
        var m = CfdiPdfModelBuilder.DesdeCartaPorte(CartaPorteDemo(TipoComprobante.Traslado), Camion, Chofer, Datos, "DEMOCP-000400");

        m.Tipo.Should().Be(TipoDocumentoPdf.CartaPorte);
        new CfdiMilletDocument(m).Titulo.Should().Be("CARTA PORTE/WAYBILL:DEMOCP 000401");
        m.TipoComprobante.Should().Be("Traslado");
        m.NombreArchivo.Should().Be("carta-porte-DEMOCP-000401.pdf");

        var cp = m.CartaPorte!;
        cp.Origen.Should().Be(new UbicacionPdf("PLANTA DEMO MÉRIDA", "97000", "YUC", new DateTimeOffset(2026, 10, 22, 8, 0, 0, TimeSpan.FromHours(-6))));
        cp.Destino.Should().Be(new UbicacionPdf("BODEGA DEMO CANCÚN", "77500", "ROO", new DateTimeOffset(2026, 10, 22, 16, 0, 0, TimeSpan.FromHours(-6))));
        cp.DistanciaKm.Should().Be(305m);
        cp.Vehiculo.Should().Be(new VehiculoPdf("ABC123D", "C2", 2022, 17.5m, "TPAF01", "SCT-DEMO-77", "SEGUROS DEMO", "POL-DEMO-1"));
        cp.Operador.Should().Be(new OperadorPdf("OPERADOR DEMO", "XAXX010101000", "LIC-DEMO-9"));
        cp.Mercancias.Should().Equal(
            new MercanciaPdf("30171700", "VIDRIO TEMPLADO DEMO", 12, "H87", 480.5m, false),
            new MercanciaPdf("31201600", "SELLADOR DEMO", 3, "XBX", 19.5m, true));
        cp.PesoBrutoTotalKg.Should().Be(500m);
        cp.CartaPortePreviaFolio.Should().Be("DEMOCP-000400");

        // Conceptos iguales al XML de traslado: las mercancías con valor 0 y sin impuestos.
        m.Conceptos.Select(c => (c.ClaveProdServ, c.Cantidad, c.Importe)).Should().Equal(("30171700", 12m, 0m), ("31201600", 3m, 0m));
        m.Impuestos.Should().BeEmpty();
        m.Totales.Total.Should().Be(0m);
        m.TotalesFisicos.Should().Be(new TotalesFisicosPdf(500m, null, 15));

        m.Timbre!.UrlQr.Should().Contain("id=00000000-0000-4000-8000-000000000401").And.Contain("tt=0000000000.000000");
        m.Cabecera.Uuid.Should().Be("00000000-0000-4000-8000-000000000401");
    }

    [Fact]
    public void Traslado_omite_datos_de_pago_e_ingreso_los_conserva_con_el_servicio_de_transporte()
    {
        var traslado = CfdiPdfModelBuilder.DesdeCartaPorte(CartaPorteDemo(TipoComprobante.Traslado), Camion, Chofer, Datos);
        var ingreso = CfdiPdfModelBuilder.DesdeCartaPorte(CartaPorteDemo(TipoComprobante.Ingreso), Camion, Chofer, Datos);

        new CfdiMilletDocument(traslado).MuestraDatosDePago.Should().BeFalse();
        new CfdiMilletDocument(ingreso).MuestraDatosDePago.Should().BeTrue();
        ingreso.TipoComprobante.Should().Be("Ingreso");
        var servicio = ingreso.Conceptos.Should().ContainSingle().Subject;
        (servicio.ClaveProdServ, servicio.ClaveUnidad, servicio.Importe).Should().Be(("78101800", "E48", 5000m));
        ingreso.Impuestos.Should().ContainSingle().Which.Should().Be(new ImpuestoResumenPdf("002 IVA", "Tasa", 0.16m, 5000m, 800m, false));
        ingreso.Totales.Total.Should().Be(5800m);
        ingreso.CartaPorte!.Mercancias.Should().HaveCount(2);
    }

    [Fact]
    public void La_seccion_imprime_rotulos_bilingues_del_tramo_y_del_transporte()
    {
        var m = CfdiPdfModelBuilder.DesdeCartaPorte(CartaPorteDemo(TipoComprobante.Traslado), Camion, Chofer, Datos);

        CfdiMilletDocument.Ubicacion(m.CartaPorte!.Origen, "Fecha de salida/Departure").Should().Equal(
            ("", "PLANTA DEMO MÉRIDA"), ("C.P./Zip code:", "97000"), ("Estado/State:", "YUC"),
            ("Fecha de salida/Departure:", "22/10/2026 08:00"));
        CfdiMilletDocument.Vehiculo(m.CartaPorte.Vehiculo).Should().Contain(
            [("Placa/Plate:", "ABC123D"), ("Peso bruto/Gross weight:", "17.50 t"), ("Permiso SCT/SCT permit:", "TPAF01 SCT-DEMO-77"),
             ("Seguro/Insurance:", "SEGUROS DEMO · POL-DEMO-1")]);
        CfdiMilletDocument.Operador(m.CartaPorte.Operador).Should().Equal(
            ("", "OPERADOR DEMO"), ("RFC:", "XAXX010101000"), ("Licencia/License:", "LIC-DEMO-9"));
        CfdiMilletDocument.Vehiculo(null).Should().Equal(("", "Sin vehículo en el catálogo"));
    }

    [Fact]
    public void Genera_el_pdf_con_Carlito_en_tipo_T_e_I_y_sin_timbrar()
    {
        var gen = new QuestPdfFacturaGenerator();
        var sinTimbrar = CartaPorteDemo(TipoComprobante.Traslado, timbrar: false);

        var muestras = new (string Nombre, PdfFacturaGenerado Pdf)[]
        {
            ("carta-porte-traslado.pdf", gen.Generar(CfdiPdfModelBuilder.DesdeCartaPorte(CartaPorteDemo(TipoComprobante.Traslado), Camion, Chofer, Datos, "DEMOCP-000400"))),
            ("carta-porte-ingreso.pdf", gen.Generar(CfdiPdfModelBuilder.DesdeCartaPorte(CartaPorteDemo(TipoComprobante.Ingreso), Camion, Chofer, Datos))),
            ("carta-porte-sin-timbrar.pdf", gen.Generar(CfdiPdfModelBuilder.DesdeCartaPorte(sinTimbrar, null, null, null))),
        };

        foreach (var (_, pdf) in muestras)
        {
            var texto = Encoding.Latin1.GetString(pdf.Contenido);
            texto.Should().StartWith("%PDF");
            texto.Should().Contain("Carlito").And.NotContain("Calibri");
            pdf.ContentType.Should().Be("application/pdf");
        }

        // Muestras para la evidencia de QA: solo si se pide la carpeta (no escribe en CI).
        if (Environment.GetEnvironmentVariable("MILLET_PDF_MUESTRAS") is { Length: > 0 } carpeta)
        {
            Directory.CreateDirectory(carpeta);
            foreach (var (nombre, pdf) in muestras)
                File.WriteAllBytes(Path.Combine(carpeta, nombre), pdf.Contenido);
        }
    }

    private static CartaPorteCfdi CartaPorteDemo(TipoComprobante tipo, bool timbrar = true)
    {
        var cp = CartaPorteCfdi.CrearBorrador(Empresa, tipo, "DEMOCP-000401", 401, Sucursal, null, null, Receptor, Emisor,
            "MXN", 2026, 10, "PLANTA DEMO MÉRIDA", "BODEGA DEMO CANCÚN", 305m, Camion.Id, Chofer.Id, null, null,
            new DateTimeOffset(2026, 10, 22, 14, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 22, 22, 0, 0, TimeSpan.Zero),
            montoServicio: tipo == TipoComprobante.Ingreso ? 5000m : 0m, tasaIvaServicio: 0.16m,
            origenCodigoPostal: "97000", origenEstado: "YUC", destinoCodigoPostal: "77500", destinoEstado: "ROO");
        cp.AgregarMercancia("VIDRIO TEMPLADO DEMO", "30171700", "H87", 12, 480.5m, false);
        cp.AgregarMercancia("SELLADOR DEMO", "31201600", "XBX", 3, 19.5m, true);
        if (timbrar)
        {
            cp.MarcarTimbradoEnProceso();
            cp.MarcarTimbrado("00000000-0000-4000-8000-000000000401", Sello, Sello, "30001000000500003456", Fecha, "SPR190613I52", null);
        }
        return cp;
    }
}
