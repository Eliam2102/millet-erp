using System.Globalization;
using Millet.Documentos;
using Millet.Documentos.Componentes;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Millet.Almacen.Application.Recepciones.Pdf;

public sealed record RenglonRecepcionImpresion(int Posicion, string Clave, string Descripcion, string Unidad, decimal Cantidad, string Ubicacion);

public sealed record RenglonRechazoImpresion(string Clave, string Descripcion, string Unidad, decimal Cantidad, string Motivo);

public sealed record ComprobanteRecepcionImpresion(
    EmpresaEncabezado? Empresa, string Folio, DateOnly Fecha, string OrdenCompra, string Proveedor,
    IReadOnlyList<RenglonRecepcionImpresion> Renglones, IReadOnlyList<RenglonRechazoImpresion> Rechazos,
    string Comentarios, string? Capturo, string? Recibio);

/// <summary>
/// Comprobante de recepción (G-10): folio, orden de compra, proveedor, partidas con cantidad y ubicación, lo rechazado en el acto,
/// y las firmas de quien capturó y de quien recibió físicamente la mercancía. Mismo molde que el comprobante de salida.
/// </summary>
public static class ComprobanteRecepcionDocumento
{
    private const string FondoComentarios = "#E6E6E6";

    public static byte[] Generar(ComprobanteRecepcionImpresion c) => Crear(c).GeneratePdf();

    public static Document Crear(ComprobanteRecepcionImpresion c) => Document.Create(documento => documento.Page(pagina =>
    {
        PdfEstilos.ConfigurarPagina(pagina);
        pagina.Header().PaddingBottom(8).Element(x => Encabezado(x, c));
        pagina.Content().Column(contenido =>
        {
            contenido.Spacing(10);
            contenido.Item().Component(new TablaRenglones<RenglonRecepcionImpresion>(c.Renglones,
            [
                new("#", r => r.Posicion.ToString("000", CultureInfo.InvariantCulture), 0.6f),
                new("Clave de artículo", r => r.Clave, 1.6f),
                new("Descripción del artículo", r => r.Descripcion, 4.5f),
                new("Ubicación", r => r.Ubicacion, 2.4f),
                new("U. medida", r => r.Unidad, 1.2f, AlineacionColumna.Centro),
                new("Cantidad", r => r.Cantidad.ToString("#,##0.00", CultureInfo.InvariantCulture), 1.2f, AlineacionColumna.Derecha),
            ]));
            if (c.Rechazos.Count > 0)
            {
                contenido.Item().Text("Rechazado en el acto (no entra al inventario; queda pendiente en la orden)").Bold();
                contenido.Item().Component(new TablaRenglones<RenglonRechazoImpresion>(c.Rechazos,
                [
                    new("Clave de artículo", r => r.Clave, 1.6f),
                    new("Descripción del artículo", r => r.Descripcion, 4.5f),
                    new("Motivo del rechazo", r => r.Motivo, 3.6f),
                    new("U. medida", r => r.Unidad, 1.2f, AlineacionColumna.Centro),
                    new("Cantidad", r => r.Cantidad.ToString("#,##0.00", CultureInfo.InvariantCulture), 1.2f, AlineacionColumna.Derecha),
                ]));
            }
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
                    fila.RelativeItem().AlignBottom().Element(x => Firma(x, "Capturó: nombre y firma", c.Capturo));
                    fila.RelativeItem().AlignBottom().Element(x => Firma(x, "Recibió físicamente: nombre y firma", c.Recibio));
                });
            });
        });
    }));

    private static void Encabezado(IContainer container, ComprobanteRecepcionImpresion c) => container.Row(fila =>
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
                .Text("RECEPCION DE MERCANCIA").FontSize(PdfEstilos.Titulo).Bold();
            doc.Item().Row(r =>
            {
                Dato(r.RelativeItem(), "Orden de compra", c.OrdenCompra);
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
                Dato(r.RelativeItem(), "Fecha de documento", c.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
                Dato(r.RelativeItem(), "Proveedor", c.Proveedor);
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
}
