using System.Globalization;
using Millet.Documentos;
using Millet.Facturacion.Application.Comprobantes.Pdf;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Pdf.CfdiMillet;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Millet.Facturacion.Infrastructure.Pdf;

/// <summary>
/// Implementación QuestPDF de <see cref="IGenerarPdfFacturaPort"/> (F2-PR2).
/// El formato carta de factura, anticipo y nota de crédito es la plantilla de
/// Millet (<see cref="CfdiMilletDocument"/>, FMT-4); el ticket térmico de la
/// factura conserva su diseño. Ambos con la fuente Carlito del kit (FMT-0b).
/// </summary>
public sealed class QuestPdfFacturaGenerator : IGenerarPdfFacturaPort
{
    static QuestPdfFacturaGenerator()
    {
        // QuestPDF Community license: uso comercial bajo $1M USD anuales.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public PdfFacturaGenerado Generar(FacturaVenta factura, FormatoPdfFactura formato)
    {
        ArgumentNullException.ThrowIfNull(factura);

        if (formato != FormatoPdfFactura.TermicaSimplificada)
            return Generar(CfdiPdfModelBuilder.DesdeFactura(factura, null));

        return new PdfFacturaGenerado(GenerarTermica(factura), "application/pdf", $"factura-{factura.Folio}-termica.pdf");
    }

    public PdfFacturaGenerado Generar(FacturaAnticipo factura) =>
        Generar(CfdiPdfModelBuilder.DesdeAnticipo(factura, null));

    public PdfFacturaGenerado Generar(NotaCredito notaCredito) =>
        Generar(CfdiPdfModelBuilder.DesdeNotaCredito(notaCredito, null));

    public PdfFacturaGenerado Generar(CfdiPdfModel modelo)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        return new PdfFacturaGenerado(new CfdiMilletDocument(modelo).GeneratePdf(), "application/pdf", modelo.NombreArchivo);
    }

    /// <summary>
    /// Bloque «Observaciones» del ticket térmico (86e3tfdw2): null si la factura no
    /// tiene observaciones. Solo representación impresa — el XML no las lleva. En el
    /// formato carta van en «Comentarios» (FMT-4).
    /// </summary>
    internal static (string Titulo, string Texto)? BloqueObservaciones(FacturaVenta f) =>
        string.IsNullOrWhiteSpace(f.Observaciones) ? null : ("Observaciones / Remarks", f.Observaciones);

    private static void MostrarObservaciones(ColumnDescriptor col, FacturaVenta f, float tamanoTexto)
    {
        if (BloqueObservaciones(f) is not var (titulo, texto))
            return;
        col.Item().PaddingTop(6).Column(c =>
        {
            c.Item().Text(titulo).Bold();
            c.Item().Text(texto).FontSize(tamanoTexto);
        });
    }

    private static string M(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture);

    private static void MostrarEmbarqueCe(ColumnDescriptor col, FacturaVenta f)
    {
        if (f.DatosEmbarqueCe is not { } ce) return;
        col.Item().Text($"Expediente: {ce.FolioExpediente} · Incoterm: {ce.Incoterm}");
        col.Item().Text($"Pedimento de exportación: {ce.NumeroPedimento} · {ce.ClavePedimento} · {ce.FechaPedimento:yyyy-MM-dd}" +
            (ce.Aduana is null ? "" : $" · Aduana {ce.Aduana} {ce.AduanaNombre}"));
        foreach (var c in ce.Contenedores)
            col.Item().Text($"Contenedor: {c.Numero} · BL o guía: {c.BlGuia} · Embarque: {c.FechaEmbarqueReal:yyyy-MM-dd}");
    }

    private static byte[] GenerarTermica(FacturaVenta f) =>
        Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(new PageSize(226, 600)); // ≈80mm de ancho
                page.Margin(8);
                PdfFuentes.Registrar();
                page.DefaultTextStyle(x => x.FontSize(8).FontFamily(PdfFuentes.Familia));

                page.Content().Column(col =>
                {
                    col.Spacing(3);
                    MostrarEmbarqueCe(col, f);
                    col.Item().AlignCenter().Text("MILLET").FontSize(12).Bold();
                    col.Item().AlignCenter().Text($"Folio: {f.Folio}");
                    col.Item().Text($"RFC emisor: {f.RfcEmisor}");
                    col.Item().Text($"Cliente: {f.ReceptorNombre}");
                    col.Item().Text($"RFC: {f.ReceptorRfc}");
                    col.Item().LineHorizontal(0.5f);

                    foreach (var l in f.Lineas.OrderBy(x => x.Posicion))
                    {
                        col.Item().Text($"{M(l.Cantidad)} x {l.Descripcion}");
                        col.Item().AlignRight().Text($"{M(l.Importe)}");
                    }

                    col.Item().LineHorizontal(0.5f);
                    col.Item().AlignRight().Text($"Subtotal: {M(f.Subtotal)}");
                    col.Item().AlignRight().Text($"IVA: {M(f.ImpuestosTrasladados)}");
                    col.Item().AlignRight().Text($"TOTAL: {f.Moneda} {M(f.Total)}").Bold();

                    MostrarObservaciones(col, f, 7);

                    if (f.Uuid is not null)
                    {
                        col.Item().PaddingTop(4).Text($"UUID: {f.Uuid}").FontSize(6);
                    }

                    col.Item().PaddingTop(4).AlignCenter().Text("Gracias por su compra").FontSize(7);
                });
            });
        }).GeneratePdf();
}
