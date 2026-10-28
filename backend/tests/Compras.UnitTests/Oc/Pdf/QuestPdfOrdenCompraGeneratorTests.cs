using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Compras.Domain.Ports.Pdf;
using Millet.Compras.Infrastructure.Oc.Pdf;
using Millet.Documentos;
using UglyToad.PdfPig;

namespace Millet.Compras.UnitTests.Oc.Pdf;

/// <summary>
/// FMT-2: el generador dibuja el formato Millet a partir del modelo de impresión. Se prueba
/// sobre el texto extraído del PDF. Con <c>MILLET_PDF_DUMP</c> (carpeta, o 1 = temporal)
/// deja los PDF de muestra para compararlos con el original de Millet.
/// </summary>
public class QuestPdfOrdenCompraGeneratorTests
{
    private static readonly QuestPdfOrdenCompraGenerator Generador = new(NullLogger<QuestPdfOrdenCompraGenerator>.Instance);

    public QuestPdfOrdenCompraGeneratorTests() => PdfLicencia.Inicializar();

    private static OrdenCompraImpresion Modelo(bool extranjero = false, decimal gastos = 0m, string moneda = "MXN")
    {
        var neto = 250m;
        var iva = extranjero ? 0m : 40m;
        var tasa = extranjero ? 0m : 16m;
        return new OrdenCompraImpresion
        {
            Folio = "OC-MID2026-000300",
            Moneda = moneda,
            FechaDocumento = new DateOnly(2026, 10, 14),
            FechaEntrega = new DateOnly(2026, 10, 28),
            Referencia = "REF-77",
            Empresa = new EmpresaEncabezado("M INDUSTRIA, S.A. DE C.V.", "Millet", "MIN010101AAA", "601",
                ["AV. LOPEZ PORTILLO 46", "CANCUN, QR, 77524"]),
            Proveedor = extranjero
                ? new ProveedorImpresion("P0900", "ACME GLASS INC", "XEXX010101000", ["1 MAIN ST", "HOUSTON, TX, USA"], "TX-998877")
                : new ProveedorImpresion("P0478", "PAULINO ESTRELLA CHIM", "EECP780305DK4", ["CALLE 65 X 8 Y 10 #235", "MERIDA, YUC"]),
            Contacto = new ContactoImpresion("RODRIGO CHAY CANO", "(999) 9302370 Ext. 106", "rodrigo.chay@millet.com.mx"),
            EntregaNombre = "M INDUSTRIA, S.A. DE C.V.",
            EntregaLineas = ["KM 10 CARRETERA FEDERAL MERIDA-MOTUL", "CONKAL, YUC"],
            UsoCfdi = "G03 · Gastos en general",
            MetodoPago = "PUE · Pago en una sola exhibición",
            FormaPago = "03 · Transferencia electrónica de fondos",
            CondicionPago = "15 DIAS",
            Incoterm = extranjero ? "FOB · Libre a bordo" : null,
            Renglones =
            [
                new("CYP12082", "LONA", "LONA 3X3 AZUL", 1m, "METRO", 125m, tasa, 125m),
                new("CYP12083", "CINTA", "CINTA GRIS", 5m, "PZA", 25m, tasa, 125m),
            ],
            Subtotal = neto,
            BaseGravable = neto + gastos,
            Impuesto = iva,
            Total = neto + gastos + iva,
            Motivo = "LONA PARA LA PROTECCION DE MATERIALES",
            Elaboro = new("RODRIGO CHAY CANO", new DateOnly(2026, 10, 14)),
            Reviso = new("ANA REVISORA", new DateOnly(2026, 10, 14)),
            Autorizo = new("LUIS AUTORIZADOR", new DateOnly(2026, 10, 15)),
        };
    }

    private static string Texto(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        return Sin(string.Join(" ", doc.GetPages().Select(p => p.Text)));
    }

    // QuestPDF coloca cada glifo por separado y PdfPig no siempre recupera los espacios:
    // se compara sin espacios.
    private static string Sin(string texto) => Regex.Replace(texto, @"\s+", "");

    private static string[] Generar(string nombre, OrdenCompraImpresion modelo, string? marca)
    {
        var resultado = Generador.GenerarAsync(modelo, marca, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal("application/pdf", resultado.ContentType);
        Assert.Equal("OC-MID2026-000300.pdf", resultado.NombreSugerido);
        if (Environment.GetEnvironmentVariable("MILLET_PDF_DUMP") is { Length: > 0 } destino)
        {
            if (destino == "1") destino = Path.Combine(Path.GetTempPath(), "millet-pdf-oc");
            Directory.CreateDirectory(destino);
            File.WriteAllBytes(Path.Combine(destino, nombre + ".pdf"), resultado.Contenido);
        }
        return [Texto(resultado.Contenido)];
    }

    private static void AfirmarComun(string t, string marca)
    {
        Assert.Contains(Sin("OC-MID2026-000300"), t);
        Assert.Contains(Sin("ORDEN DE COMPRA"), t);
        Assert.Contains(Sin("CYP12082"), t);
        Assert.Contains(Sin("Su contacto"), t);
        Assert.Contains(Sin("Dirección de entrega"), t);
        Assert.Contains(Sin("Características de factura"), t);
        Assert.Contains(Sin("Condiciones de pago: 15 DIAS"), t);
        Assert.Contains(Sin("Subtotal del pedido"), t);
        Assert.Contains(Sin("Total antes del impuesto"), t);
        Assert.Contains(Sin("Importe total del impuesto"), t);
        Assert.Contains(Sin("Elaboró"), t);
        Assert.Contains(Sin("Revisó"), t);
        Assert.Contains(Sin("Autorizó"), t);
        Assert.Contains(Sin(marca), t);
        Assert.Contains(Sin(QuestPdfOrdenCompraGenerator.NotaFija), t);
        Assert.DoesNotMatch("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-", t);
        Assert.DoesNotContain(Sin("Gastos adicionales"), t);
    }

    [Fact]
    public void Nacional_ImprimeFormatoMillet_SinTaxIdNiIncoterm()
    {
        var t = Generar("oc-nacional", Modelo(), "ORIGINAL")[0];

        AfirmarComun(t, "ORIGINAL");
        Assert.Contains(Sin("EECP780305DK4"), t);
        Assert.Contains(Sin("P0478"), t);
        Assert.Contains(Sin("290.00"), t);
        Assert.DoesNotContain(Sin("Tax ID"), t);
        Assert.DoesNotContain(Sin("INCOTERM"), t);
    }

    [Fact]
    public void ExtranjeroUsd_ImprimeTaxIdEIncoterm()
    {
        var t = Generar("oc-extranjero-usd", Modelo(extranjero: true, moneda: "USD"), "COPIA 1 · 15/10/2026")[0];

        AfirmarComun(t, "COPIA 1 · 15/10/2026");
        Assert.Contains(Sin("XEXX010101000"), t);
        Assert.Contains(Sin("Tax ID: TX-998877"), t);
        Assert.Contains(Sin("INCOTERM: FOB"), t);
        Assert.Contains(Sin("250.00 USD"), t);
        Assert.Contains(Sin("Moneda: USD"), t);
    }

    [Fact]
    public void GastosAdicionales_SumanALaBaseSinImprimirseComoRenglon()
    {
        var t = Generar("oc-con-gastos", Modelo(gastos: 50m), "ORIGINAL")[0];

        AfirmarComun(t, "ORIGINAL");
        Assert.Contains(Sin("300.00"), t); // total antes del impuesto: incluye los gastos
        Assert.Contains(Sin("340.00"), t);
    }

    [Fact]
    public void VistaPrevia_NoLlevaMarca()
    {
        var t = Generar("oc-vista-previa", Modelo(), null)[0];

        Assert.DoesNotContain(Sin("ORIGINAL"), t);
        Assert.DoesNotContain(Sin("COPIA"), t);
    }
}
