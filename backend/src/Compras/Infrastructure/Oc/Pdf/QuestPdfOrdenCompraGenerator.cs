using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Millet.Compras.Infrastructure.Oc.Pdf;

/// <summary>
/// Implementación real (F6-PR3) de <see cref="IGenerarPdfOrdenCompraPort"/>
/// usando QuestPDF. Produce un PDF institucional con:
/// <list type="bullet">
///   <item>Cabecera con folio, fecha, moneda y placeholders de datos
///         fiscales Millet (PLATFORM-TODO razón social/RFC reales).</item>
///   <item>Bloque proveedor (placeholder; F7+ lo enriquecerá con datos de
///         catálogo).</item>
///   <item>Tabla de líneas agrupadas por artículo (§3.bis.4 cosmético —
///         oculta departamento solicitante / requisición al proveedor).</item>
///   <item>Bloque de totales con subtotal, descuento, gastos, IVA y total
///         a pagar.</item>
/// </list>
///
/// <para>
/// Reemplaza <see cref="LocalPdfOrdenCompraStub"/>. La selección entre
/// real y stub vive en <c>Program.cs</c> (DI).
/// </para>
/// </summary>
public sealed class QuestPdfOrdenCompraGenerator : IGenerarPdfOrdenCompraPort
{
    public const string GeneradorId = "QuestPdfOrdenCompraGenerator";

    private readonly ILogger<QuestPdfOrdenCompraGenerator> _logger;

    static QuestPdfOrdenCompraGenerator()
    {
        // QuestPDF Community license: válido para uso comercial bajo $1M USD
        // anuales. Tiglass/Millet cumple esta condición para MVP.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public QuestPdfOrdenCompraGenerator(ILogger<QuestPdfOrdenCompraGenerator> logger)
    {
        _logger = logger;
    }

    public Task<PdfOrdenCompraGenerado> GenerarAsync(
        Millet.Compras.Domain.Oc.OrdenCompra oc, CancellationToken cancellationToken)
    {
        var totales = oc.CalcularTotales();
        var lineasAgrupadas = oc.Lineas
            .GroupBy(l => l.ArticuloId)
            .Select(g => new LineaPdf(
                ArticuloId: g.Key,
                Cantidad: g.Sum(l => l.Cantidad),
                UnidadMedida: g.First().UnidadMedida,
                PrecioUnitario: g.First().PrecioUnitario,
                Subtotal: g.Sum(l => l.SubtotalLinea)))
            .ToList();

        var bytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Calibri));

                page.Header().Column(col =>
                {
                    col.Item().Text("MILLET — ORDEN DE COMPRA")
                        .FontSize(16).Bold();
                    col.Item().PaddingTop(2).Text("Vidrio de valor agregado — México")
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Folio").Bold();
                            c.Item().Text(oc.Folio.Valor);
                            c.Item().Text($"Año: {oc.FolioAnio}").FontSize(9);
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Fecha documento").Bold();
                            c.Item().Text(oc.FechaDocumento.ToString("yyyy-MM-dd"));
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Moneda").Bold();
                            c.Item().Text(oc.Moneda);
                            if (oc.TipoCambio is decimal tc)
                            {
                                c.Item().Text($"Tipo cambio: {tc:F4}").FontSize(9);
                            }
                        });
                    });

                    col.Item().PaddingTop(4).Text("Proveedor").Bold();
                    col.Item().Text($"ID: {oc.ProveedorId}").FontSize(9);
                    // PLATFORM-TODO(<CatalogoProveedoresLookup>): cuando el
                    // PDF impl tenga acceso al catálogo, renderizar razón
                    // social / RFC / dirección reales.

                    col.Item().PaddingTop(8).Text("Detalle").Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Artículo").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Cant.").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("P.U.").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Subtotal").Bold();
                        });

                        foreach (var l in lineasAgrupadas)
                        {
                            table.Cell().Padding(4).Text(l.ArticuloId.ToString());
                            table.Cell().Padding(4).Text($"{l.Cantidad} {l.UnidadMedida}");
                            table.Cell().Padding(4).Text($"{l.PrecioUnitario:F2}");
                            table.Cell().Padding(4).Text($"{l.Subtotal:F2}");
                        }
                    });

                    col.Item().PaddingTop(8).AlignRight().Column(c =>
                    {
                        c.Spacing(2);
                        c.Item().Text($"Subtotal: {totales.SubtotalAntesDescuento:F2} {oc.Moneda}");
                        if (totales.DescuentoGlobalAplicado > 0m)
                        {
                            c.Item().Text($"Descuento global: -{totales.DescuentoGlobalAplicado:F2}");
                        }
                        if (totales.GastosAdicionales > 0m)
                        {
                            c.Item().Text($"Gastos adicionales: {totales.GastosAdicionales:F2}");
                        }
                        c.Item().Text($"IVA: {totales.IvaTotal:F2}");
                        if (totales.RetencionIsrTotal > 0m)
                        {
                            c.Item().Text($"Retención ISR: -{totales.RetencionIsrTotal:F2}");
                        }
                        if (totales.Redondeo != 0m)
                        {
                            c.Item().Text($"Redondeo: {totales.Redondeo:F2}");
                        }
                        c.Item().PaddingTop(4).Text($"TOTAL: {totales.TotalAPagar:F2} {oc.Moneda}")
                            .FontSize(12).Bold();
                    });

                    if (!string.IsNullOrWhiteSpace(oc.Observaciones))
                    {
                        col.Item().PaddingTop(12).Text("Observaciones").Bold();
                        col.Item().Text(oc.Observaciones);
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Página ").FontSize(9);
                    text.CurrentPageNumber().FontSize(9);
                    text.Span(" de ").FontSize(9);
                    text.TotalPages().FontSize(9);
                });
            });
        }).GeneratePdf();

        _logger.LogInformation(
            "PDF generado (QuestPDF). OcId={OcId} Folio={Folio} Tamaño={Bytes}",
            oc.Id, oc.Folio.Valor, bytes.Length);

        return Task.FromResult(new PdfOrdenCompraGenerado(
            Contenido: bytes,
            ContentType: "application/pdf",
            NombreSugerido: $"{oc.Folio.Valor}.pdf"));
    }

    private sealed record LineaPdf(
        Guid ArticuloId,
        decimal Cantidad,
        string UnidadMedida,
        decimal PrecioUnitario,
        decimal Subtotal);
}
