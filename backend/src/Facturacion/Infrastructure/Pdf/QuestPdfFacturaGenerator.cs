using System.Globalization;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Ports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Millet.Facturacion.Infrastructure.Pdf;

/// <summary>
/// Implementación QuestPDF de <see cref="IGenerarPdfFacturaPort"/> (F2-PR2).
/// Produce el PDF de la factura de venta en formato bilingüe (carta) o térmico
/// simplificado. Mientras el timbrado sea stub el bloque del timbre muestra el
/// UUID/sello fake; con el timbrado real (F12) refleja los datos del SAT.
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

        var bytes = formato == FormatoPdfFactura.TermicaSimplificada
            ? GenerarTermica(factura)
            : GenerarBilingue(factura);

        var sufijo = formato == FormatoPdfFactura.TermicaSimplificada ? "termica" : "bilingue";
        return new PdfFacturaGenerado(bytes, "application/pdf", $"factura-{factura.Folio}-{sufijo}.pdf");
    }

    public PdfFacturaGenerado Generar(FacturaAnticipo factura)
    {
        ArgumentNullException.ThrowIfNull(factura);
        var bytes = GenerarConceptoUnico(
            factura,
            titulo: "MILLET — FACTURA DE ANTICIPO / ADVANCE PAYMENT INVOICE",
            claveProdServ: factura.ClaveProdServSat,
            descripcion: factura.Descripcion,
            motivo: null,
            relaciones: null);
        return new PdfFacturaGenerado(bytes, "application/pdf", $"factura-anticipo-{factura.Folio}.pdf");
    }

    public PdfFacturaGenerado Generar(NotaCredito notaCredito)
    {
        ArgumentNullException.ThrowIfNull(notaCredito);
        var bytes = GenerarConceptoUnico(
            notaCredito,
            titulo: "MILLET — NOTA DE CRÉDITO / CREDIT NOTE",
            claveProdServ: notaCredito.ClaveProdServSat,
            descripcion: notaCredito.Descripcion,
            motivo: notaCredito.Motivo.ToString(),
            relaciones: notaCredito.Relaciones);
        return new PdfFacturaGenerado(bytes, "application/pdf", $"nota-credito-{notaCredito.Folio}.pdf");
    }

    private static string M(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>
    /// Layout bilingüe carta para comprobantes de concepto único (factura de
    /// anticipo y NC, 13-C): misma cabecera emisor/receptor y bloque de timbre
    /// que la factura de venta, con una tabla de un solo renglón y — para la
    /// NC — el motivo y la tabla de relaciones CFDI (01/07).
    /// </summary>
    private static byte[] GenerarConceptoUnico(
        Comprobante c,
        string titulo,
        string claveProdServ,
        string descripcion,
        string? motivo,
        IReadOnlyCollection<RelacionCfdi>? relaciones) =>
        Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.Calibri));

                page.Header().Column(col =>
                {
                    col.Item().Text(titulo).FontSize(15).Bold();
                    col.Item().Text("Vidrio de valor agregado — México").FontSize(8).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().PaddingVertical(8).Column(col =>
                {
                    col.Spacing(6);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(x =>
                        {
                            x.Item().Text("Emisor / Issuer").Bold();
                            x.Item().Text($"RFC: {c.RfcEmisor}");
                            x.Item().Text($"Régimen / Regime: {c.RegimenFiscalEmisor}");
                        });
                        row.RelativeItem().Column(x =>
                        {
                            x.Item().Text("Receptor / Recipient").Bold();
                            x.Item().Text($"RFC: {c.ReceptorRfc}");
                            x.Item().Text(c.ReceptorNombre);
                            x.Item().Text($"Uso CFDI / Use: {c.ReceptorUsoCfdi}  ·  CP: {c.ReceptorCodigoPostal}");
                        });
                    });

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"Folio: {c.Folio}");
                        row.RelativeItem().Text($"Moneda / Currency: {c.Moneda}");
                        row.RelativeItem().Text($"Método/Forma pago: {c.MetodoPago} / {c.FormaPago}");
                    });

                    if (motivo is not null)
                        col.Item().Text($"Motivo / Reason: {motivo}").Italic();

                    col.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(x =>
                        {
                            x.ConstantColumn(50);   // cant
                            x.RelativeColumn();     // desc
                            x.ConstantColumn(70);   // importe
                        });
                        table.Header(h =>
                        {
                            h.Cell().Text("Cant / Qty").Bold();
                            h.Cell().Text("Descripción / Description").Bold();
                            h.Cell().AlignRight().Text("Importe / Amount").Bold();
                        });
                        table.Cell().Text("1");
                        table.Cell().Text($"[{claveProdServ}] {descripcion}");
                        table.Cell().AlignRight().Text(M(c.Subtotal));
                    });

                    col.Item().AlignRight().PaddingTop(6).Column(t =>
                    {
                        t.Item().Text($"Subtotal: {c.Moneda} {M(c.Subtotal)}");
                        t.Item().Text($"IVA / VAT: {c.Moneda} {M(c.ImpuestosTrasladados)}");
                        t.Item().Text($"TOTAL: {c.Moneda} {M(c.Total)}").Bold().FontSize(11);
                    });

                    if (relaciones is { Count: > 0 })
                    {
                        col.Item().PaddingTop(8).Column(x =>
                        {
                            x.Item().Text("CFDI relacionados / Related CFDI").Bold();
                            foreach (var r in relaciones)
                                x.Item().Text($"[{r.TipoRelacion}] {r.UuidRelacionado}").FontSize(8);
                        });
                    }

                    if (c.Estado == EstadoTimbrado.Timbrado && c.Uuid is not null)
                    {
                        col.Item().PaddingTop(8).Column(x =>
                        {
                            x.Item().Text("Timbre fiscal / Tax stamp").Bold();
                            x.Item().Text($"Folio fiscal (UUID): {c.Uuid}").FontSize(8);
                            x.Item().Text($"Fecha timbrado: {c.FechaTimbrado:O}").FontSize(8);
                            x.Item().Text($"No. certificado SAT: {c.NoCertificadoSat}").FontSize(8);
                        });
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Este documento es una representación impresa de un CFDI. / Printed representation of a CFDI.")
                        .FontSize(7).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();

    private static byte[] GenerarBilingue(FacturaVenta f) =>
        Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.Calibri));

                page.Header().Column(col =>
                {
                    col.Item().Text("MILLET — FACTURA / INVOICE").FontSize(15).Bold();
                    col.Item().Text("Vidrio de valor agregado — México").FontSize(8).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().PaddingVertical(8).Column(col =>
                {
                    col.Spacing(6);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Emisor / Issuer").Bold();
                            c.Item().Text($"RFC: {f.RfcEmisor}");
                            c.Item().Text($"Régimen / Regime: {f.RegimenFiscalEmisor}");
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Receptor / Recipient").Bold();
                            c.Item().Text($"RFC: {f.ReceptorRfc}");
                            c.Item().Text(f.ReceptorNombre);
                            c.Item().Text($"Uso CFDI / Use: {f.ReceptorUsoCfdi}  ·  CP: {f.ReceptorCodigoPostal}");
                        });
                    });

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"Folio: {f.Folio}");
                        row.RelativeItem().Text($"Moneda / Currency: {f.Moneda}");
                        row.RelativeItem().Text($"Método/Forma pago: {f.MetodoPago} / {f.FormaPago}");
                    });

                    col.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(50);   // cant
                            c.RelativeColumn();      // desc
                            c.ConstantColumn(70);    // p.unit
                            c.ConstantColumn(60);    // desc
                            c.ConstantColumn(70);    // importe
                        });
                        table.Header(h =>
                        {
                            h.Cell().Text("Cant / Qty").Bold();
                            h.Cell().Text("Descripción / Description").Bold();
                            h.Cell().AlignRight().Text("P.Unit").Bold();
                            h.Cell().AlignRight().Text("Desc.").Bold();
                            h.Cell().AlignRight().Text("Importe / Amount").Bold();
                        });
                        foreach (var l in f.Lineas.OrderBy(x => x.Posicion))
                        {
                            table.Cell().Text(M(l.Cantidad));
                            table.Cell().Text($"[{l.ClaveProdServSat}] {l.Descripcion}");
                            table.Cell().AlignRight().Text(M(l.ValorUnitario));
                            table.Cell().AlignRight().Text(M(l.Descuento));
                            table.Cell().AlignRight().Text(M(l.Importe));
                        }
                    });

                    col.Item().AlignRight().PaddingTop(6).Column(t =>
                    {
                        t.Item().Text($"Subtotal: {f.Moneda} {M(f.Subtotal)}");
                        t.Item().Text($"Descuento / Discount: {f.Moneda} {M(f.Descuento)}");
                        t.Item().Text($"IVA / VAT: {f.Moneda} {M(f.ImpuestosTrasladados)}");
                        t.Item().Text($"Retenciones / Withholdings: {f.Moneda} {M(f.Retenciones)}");
                        t.Item().Text($"TOTAL: {f.Moneda} {M(f.Total)}").Bold().FontSize(11);
                    });

                    if (f.Estado == EstadoTimbrado.Timbrado && f.Uuid is not null)
                    {
                        col.Item().PaddingTop(8).Column(c =>
                        {
                            c.Item().Text("Timbre fiscal / Tax stamp").Bold();
                            c.Item().Text($"Folio fiscal (UUID): {f.Uuid}").FontSize(8);
                            c.Item().Text($"Fecha timbrado: {f.FechaTimbrado:O}").FontSize(8);
                            c.Item().Text($"No. certificado SAT: {f.NoCertificadoSat}").FontSize(8);
                        });
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Este documento es una representación impresa de un CFDI. / Printed representation of a CFDI.")
                        .FontSize(7).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();

    private static byte[] GenerarTermica(FacturaVenta f) =>
        Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(new PageSize(226, 600)); // ≈80mm de ancho
                page.Margin(8);
                page.DefaultTextStyle(x => x.FontSize(8).FontFamily(Fonts.Calibri));

                page.Content().Column(col =>
                {
                    col.Spacing(3);
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

                    if (f.Uuid is not null)
                    {
                        col.Item().PaddingTop(4).Text($"UUID: {f.Uuid}").FontSize(6);
                    }

                    col.Item().PaddingTop(4).AlignCenter().Text("Gracias por su compra").FontSize(7);
                });
            });
        }).GeneratePdf();
}
