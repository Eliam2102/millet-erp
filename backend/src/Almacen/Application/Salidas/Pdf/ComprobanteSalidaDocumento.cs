using System.Globalization;
using Millet.Documentos;
using Millet.Documentos.Componentes;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Millet.Almacen.Application.Salidas.Pdf;

public sealed record RenglonSalidaImpresion(int Posicion, string Clave, string Descripcion, string Unidad, decimal Cantidad);

public sealed record ComprobanteSalidaImpresion(
    EmpresaEncabezado? Empresa, string Folio, DateOnly FechaDocumento, DateOnly? FechaContabilizado,
    IReadOnlyList<RenglonSalidaImpresion> Renglones, string Comentarios, string? Elaboro, string? Recibio);

/// <summary>
/// Comprobante de salida con el formato M2 de Millet («SALIDA DE MERCANCIA», 28-oct): sin costos,
/// con la RQ y a quién se entrega en comentarios, y firmas a mano de Elaboró y Recibió más la huella.
/// La licencia de QuestPDF la fija el host (<c>PdfLicencia</c>).
/// </summary>
public static class ComprobanteSalidaDocumento
{
    private const string FondoComentarios = "#E6E6E6";

    public static byte[] Generar(ComprobanteSalidaImpresion c) => Crear(c).GeneratePdf();

    public static Document Crear(ComprobanteSalidaImpresion c) => Document.Create(documento => documento.Page(pagina =>
    {
        PdfEstilos.ConfigurarPagina(pagina);
        pagina.Header().PaddingBottom(8).Element(x => Encabezado(x, c));
        pagina.Content().Column(contenido =>
        {
            contenido.Spacing(10);
            contenido.Item().Component(new TablaRenglones<RenglonSalidaImpresion>(c.Renglones,
            [
                new("#", r => r.Posicion.ToString("000", CultureInfo.InvariantCulture), 0.6f),
                new("Clave de artículo", r => r.Clave, 1.6f),
                new("Descripción del artículo", r => r.Descripcion, 6),
                new("U. medida", r => r.Unidad, 1.2f, AlineacionColumna.Centro),
                new("Cantidad", r => r.Cantidad.ToString("#,##0.00", CultureInfo.InvariantCulture), 1.2f, AlineacionColumna.Derecha),
            ]));
            contenido.Item().ShowEntire().Column(pie =>
            {
                pie.Spacing(18);
                pie.Item().Background(FondoComentarios).Padding(6).Column(com =>
                {
                    com.Item().Text("Comentarios del documento:");
                    com.Item().Text(c.Comentarios);
                });
                pie.Item().Row(fila =>
                {
                    fila.Spacing(24);
                    fila.RelativeItem().AlignBottom().Element(x => Firma(x, "Elaboró: nombre y firma", c.Elaboro));
                    fila.RelativeItem().AlignBottom().Element(x => Firma(x, "Recibió: nombre y firma", c.Recibio));
                    fila.ConstantItem(3, Unit.Centimetre).Column(h =>
                    {
                        h.Item().Height(2.6f, Unit.Centimetre).Border(0.75f);
                        h.Item().AlignCenter().Text("Huella").Bold();
                    });
                });
            });
        });
    }));

    private static void Encabezado(IContainer container, ComprobanteSalidaImpresion c) => container.Row(fila =>
    {
        fila.Spacing(12);
        var logo = c.Empresa?.Logo;
        if (logo is { Length: > 0 })
            fila.ConstantItem(PdfEstilos.LogoAnchoCm, Unit.Centimetre).MaxHeight(2.5f, Unit.Centimetre).Image(logo).FitArea();
        fila.RelativeItem().Column(empresa =>
        {
            if (c.Empresa is null) return;
            empresa.Item().Text(c.Empresa.RazonSocial).Bold();
            foreach (var linea in c.Empresa.DomicilioLineas) empresa.Item().Text(linea);
        });
        fila.ConstantItem(8.5f, Unit.Centimetre).Column(doc =>
        {
            doc.Spacing(4);
            doc.Item().AlignRight().BorderLeft(4).BorderColor(PdfEstilos.GrisBarra).PaddingLeft(5)
                .Text("SALIDA DE MERCANCIA").FontSize(PdfEstilos.Titulo).Bold();
            doc.Item().Row(r =>
            {
                Dato(r.RelativeItem(), "Referencia", "");
                Dato(r.RelativeItem(), "Número de documento", c.Folio);
                r.ConstantItem(1.4f, Unit.Centimetre).AlignRight().Column(p =>
                {
                    p.Item().AlignRight().Text("Página");
                    p.Item().AlignRight().Text(t =>
                    {
                        t.CurrentPageNumber().Bold();
                        t.Span("/").Bold();
                        t.TotalPages().Bold();
                    });
                });
            });
            doc.Item().Row(r =>
            {
                Dato(r.RelativeItem(), "Fecha de documento", Fecha(c.FechaDocumento));
                Dato(r.RelativeItem(), "Fecha de contabilizado:", c.FechaContabilizado is { } f ? Fecha(f) : "");
            });
        });
    });

    private static void Dato(IContainer container, string etiqueta, string valor) => container.Column(d =>
    {
        d.Item().AlignCenter().Text(etiqueta);
        d.Item().AlignCenter().Text(valor).Bold();
    });

    private static void Firma(IContainer container, string etiqueta, string? nombre) => container.Column(f =>
    {
        // El nombre va impreso arriba de la línea; la firma se pone a mano sobre él.
        f.Item().PaddingTop(28).AlignCenter().Text(nombre ?? "").FontSize(PdfEstilos.Detalle);
        f.Item().BorderTop(0.75f).PaddingTop(2).AlignCenter().Text(etiqueta).Bold();
    });

    private static string Fecha(DateOnly fecha) => fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
