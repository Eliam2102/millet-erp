using System.Globalization;
using Millet.Documentos;
using Millet.Facturacion.Domain.Ports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Millet.Facturacion.Infrastructure.Pdf.ValeCaja;

/// <summary>
/// Vale de Caja con el formato de Millet (86e3ww6tw, formato del 28-oct): recuadro
/// de logo y título, folio y fecha, la frase «Recibí de … por concepto de …», forma
/// de pago con casillas, firmas de Elaboró y Recibí, y el recuadro de Fecha de
/// impresión, Vo.Bo. de Tesorería y Liquidado el día. Carta, fuente Carlito del
/// kit (como FMT-4). Solo presenta el <see cref="ValeCajaPdfModel"/>.
/// </summary>
public sealed class ValeCajaDocument(ValeCajaPdfModel m) : IDocument
{
    private const string Leyenda =
        "Este documento es un comprobante interno y deberá conservarse con el soporte correspondiente.";
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Vale de Caja {m.Folio}",
        Author = m.EmpresaNombre,
        Creator = "Millet ERP",
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            PdfEstilos.ConfigurarPagina(page);
            page.MarginHorizontal(EstilosValeCaja.MargenLateralPulgadas, Unit.Inch);
            page.MarginVertical(EstilosValeCaja.MargenVerticalPulgadas, Unit.Inch);
            // Sin ligaduras «ti/fi/fl» de Carlito: el texto del vale se puede buscar y copiar (igual que la OC).
            page.DefaultTextStyle(x => x.FontFamily(PdfFuentes.Familia).FontSize(EstilosValeCaja.Texto).FontColor(EstilosValeCaja.Negro)
                .DisableFontFeature(FontFeatures.StandardLigatures));

            page.Content().Column(col =>
            {
                col.Item().Element(Encabezado);
                col.Item().PaddingTop(EstilosValeCaja.EspacioFolio).Element(FolioYFecha);
                col.Item().PaddingTop(EstilosValeCaja.EspacioRecibi).Element(Recibi);
                col.Item().PaddingTop(EstilosValeCaja.EspacioFormaPago).Element(FormaDePago);
                col.Item().PaddingTop(EstilosValeCaja.EspacioFirmas).Element(Firmas);
                col.Item().PaddingTop(EstilosValeCaja.EspacioRecuadro).Element(RecuadroTesoreria);
                col.Item().PaddingTop(6).AlignCenter().Text(Leyenda).FontSize(EstilosValeCaja.Leyenda);
            });
        });
    }

    internal static string Fecha(DateOnly fecha) => fecha.ToString("dd / MM / yyyy", CultureInfo.InvariantCulture);

    internal string Importe => m.Importe.ToString("N2", Mx);

    private void Encabezado(IContainer container)
    {
        container.Height(EstilosValeCaja.EncabezadoAlto).Border(EstilosValeCaja.Linea).BorderColor(EstilosValeCaja.Borde).Row(fila =>
        {
            fila.RelativeItem(EstilosValeCaja.ColumnaLogo).BorderRight(EstilosValeCaja.Linea).BorderColor(EstilosValeCaja.Borde)
                .Padding(6).AlignMiddle().AlignCenter().Column(logo =>
                {
                    if (m.Logo is { Length: > 0 } imagen)
                        logo.Item().AlignCenter().MaxHeight(EstilosValeCaja.LogoAlto).MaxWidth(EstilosValeCaja.LogoAncho).Image(imagen).FitArea();
                    logo.Item().PaddingTop(2).AlignCenter().Text(m.EmpresaNombre).FontSize(EstilosValeCaja.Empresa);
                });
            fila.RelativeItem(EstilosValeCaja.ColumnaTitulo).AlignMiddle().AlignCenter().Column(titulo =>
            {
                titulo.Item().AlignCenter().Text("VALE DE CAJA").Bold().FontSize(EstilosValeCaja.Titulo);
                titulo.Item().PaddingTop(8).AlignCenter().Text("Comprobante interno de entrega de efectivo").FontSize(EstilosValeCaja.Subtitulo);
            });
        });
    }

    private void FolioYFecha(IContainer container)
    {
        container.Row(fila =>
        {
            fila.RelativeItem().Text(t =>
            {
                t.Span("Folio: ").Bold();
                t.Span(m.Folio.ToString(CultureInfo.InvariantCulture)).Bold();
            });
            fila.RelativeItem().Text(t =>
            {
                t.Span("Fecha de expedición: ").Bold();
                t.Span(Fecha(m.FechaExpedicion));
            });
        });
    }

    private void Recibi(IContainer container)
    {
        container.Text(t =>
        {
            t.ParagraphSpacing(0);
            t.Span("Recibí de ").Bold();
            t.Span(m.Cajero).Bold();
            t.Span(" la cantidad de $ ").Bold();
            t.Span(Importe);
            t.Span(" M.N. ").Bold();
            t.Span($"({m.ImporteEnLetra})").Bold();
            t.Span(" por concepto de ").Bold();
            t.Span(m.Concepto).Bold();
        });
    }

    private void FormaDePago(IContainer container)
    {
        container.Row(fila =>
        {
            fila.AutoItem().AlignMiddle().Text("Forma de pago:").Bold();
            fila.ConstantItem(12);
            fila.AutoItem().AlignMiddle().Element(c => Casilla(c, m.FormaPago == FormaPagoVale.Efectivo));
            fila.AutoItem().AlignMiddle().PaddingLeft(3).Text("Efectivo");
            fila.ConstantItem(12);
            fila.AutoItem().AlignMiddle().Element(c => Casilla(c, m.FormaPago == FormaPagoVale.Cheque));
            fila.AutoItem().AlignMiddle().PaddingLeft(3).Text("Cheque");
            fila.ConstantItem(12);
            fila.AutoItem().AlignMiddle().Element(c => Casilla(c, m.FormaPago == FormaPagoVale.Otro));
            fila.AutoItem().AlignMiddle().PaddingLeft(3).Text("Otro: ");
            fila.ConstantItem(EstilosValeCaja.OtroAncho).AlignBottom().BorderBottom(EstilosValeCaja.Linea)
                .BorderColor(EstilosValeCaja.Negro).Text(m.FormaPagoOtro ?? string.Empty);
        });
    }

    /// <summary>Casilla «☐»; con una X cuando es la forma del retiro.</summary>
    private static void Casilla(IContainer container, bool marcada)
    {
        var caja = container.Width(EstilosValeCaja.Casilla).Height(EstilosValeCaja.Casilla)
            .Border(0.75f).BorderColor(EstilosValeCaja.Negro);
        if (marcada)
            caja.AlignCenter().AlignMiddle().Text("X").Bold().FontSize(EstilosValeCaja.Casilla - 1).LineHeight(1);
    }

    private void Firmas(IContainer container)
    {
        container.Row(fila =>
        {
            fila.RelativeItem().Element(c => Firma(c, "Elaboró", m.Cajero));
            fila.RelativeItem().Element(c => Firma(c, "Recibí", "Nombre y firma"));
        });
    }

    private static void Firma(IContainer container, string titulo, string pie)
    {
        container.AlignCenter().Width(EstilosValeCaja.FirmaAncho).Column(col =>
        {
            col.Item().BorderBottom(EstilosValeCaja.Linea).BorderColor(EstilosValeCaja.Negro).Height(1);
            col.Item().PaddingTop(4).AlignCenter().Text(titulo).Bold().FontSize(EstilosValeCaja.Firma);
            col.Item().PaddingTop(2).AlignCenter().Text(pie).FontSize(EstilosValeCaja.Firma);
        });
    }

    private void RecuadroTesoreria(IContainer container)
    {
        container.Border(EstilosValeCaja.Linea).BorderColor(EstilosValeCaja.Borde).Column(col =>
        {
            col.Item().Height(EstilosValeCaja.FilaImpresionAlto).PaddingHorizontal(6).AlignMiddle().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(EstilosValeCaja.Firma));
                t.Span("Fecha de impresión: ").Bold();
                t.Span(Fecha(m.FechaImpresion));
            });
            col.Item().BorderTop(EstilosValeCaja.Linea).BorderColor(EstilosValeCaja.Borde)
                .Height(EstilosValeCaja.FilaFirmaAlto).Padding(6).Text("Vo.Bo. de Tesorería").Bold();
            col.Item().BorderTop(EstilosValeCaja.Linea).BorderColor(EstilosValeCaja.Borde)
                .Height(EstilosValeCaja.FilaFirmaAlto).Padding(6).Text("Liquidado el día:").Bold();
        });
    }
}
