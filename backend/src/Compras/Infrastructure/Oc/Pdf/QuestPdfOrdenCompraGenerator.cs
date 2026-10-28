using System.Globalization;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Ports.Pdf;
using Millet.Documentos;
using Millet.Documentos.Componentes;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Millet.Compras.Infrastructure.Oc.Pdf;

/// <summary>
/// Implementación de <see cref="IGenerarPdfOrdenCompraPort"/> con QuestPDF en el formato de
/// orden de compra de Millet (FMT-2): encabezado con la empresa a la izquierda y la caja del
/// documento a la derecha, bloques proveedor/contacto/entrega/factura, tabla de renglones,
/// totales, motivo, firmas y la nota fija. Solo dibuja: el modelo ya viene resuelto.
/// Reutiliza el kit de <c>Millet.Documentos</c>; la licencia la fija el host (<c>PdfLicencia</c>).
/// </summary>
public sealed class QuestPdfOrdenCompraGenerator : IGenerarPdfOrdenCompraPort
{
    public const string GeneradorId = "QuestPdfOrdenCompraGenerator";
    public const string NotaFija = "Nota: \"Las facturas deberán indicar el número de orden de compra\"";

    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    private readonly ILogger<QuestPdfOrdenCompraGenerator> _logger;

    public QuestPdfOrdenCompraGenerator(ILogger<QuestPdfOrdenCompraGenerator> logger)
    {
        _logger = logger;
    }

    public Task<PdfOrdenCompraGenerado> GenerarAsync(
        OrdenCompraImpresion modelo, string? marca, CancellationToken cancellationToken)
    {
        var oc = modelo;
        var bytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                PdfEstilos.ConfigurarPagina(page);
                // Carlito liga "ti"/"tt": sin esto el texto del PDF no se puede buscar ni copiar ("Can dad").
                page.Header().DefaultTextStyle(SinLigaduras).Element(c => Encabezado(c, oc, marca));
                page.Content().DefaultTextStyle(SinLigaduras).PaddingVertical(8).Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Element(c => Bloques(c, oc));
                    col.Item().Element(c => Renglones(c, oc));
                    col.Item().ShowEntire().Element(c => Totales(c, oc));
                    if (!string.IsNullOrWhiteSpace(oc.Motivo))
                        col.Item().Text(oc.Motivo);
                    col.Item().ShowEntire().PaddingTop(20).Row(fila =>
                    {
                        fila.Spacing(12);
                        Firma(fila.RelativeItem(), "Elaboró:", oc.Elaboro);
                        Firma(fila.RelativeItem(), "Revisó:", oc.Reviso);
                        Firma(fila.RelativeItem(), "Autorizó:", oc.Autorizo);
                    });
                    col.Item().PaddingTop(10).Text(NotaFija);
                });
            });
        }).GeneratePdf();

        _logger.LogInformation(
            "PDF generado (QuestPDF). Folio={Folio} Tamaño={Bytes}", oc.Folio, bytes.Length);

        return Task.FromResult(new PdfOrdenCompraGenerado(
            Contenido: bytes,
            ContentType: "application/pdf",
            NombreSugerido: $"{oc.Folio}.pdf"));
    }

    private static TextStyle SinLigaduras(TextStyle estilo) => estilo.DisableFontFeature(FontFeatures.StandardLigatures);

    private static void Encabezado(IContainer container, OrdenCompraImpresion oc, string? marca)
    {
        container.Row(fila =>
        {
            fila.Spacing(12);
            // Empresa a la izquierda (el EncabezadoEmpresa del kit la pone a la derecha).
            fila.RelativeItem().Column(izq =>
            {
                var logo = oc.Empresa.Logo;
                if (logo is { Length: > 0 })
                    izq.Item().MaxHeight(PdfEstilos.LogoAnchoCm, Unit.Centimetre)
                        .MaxWidth(PdfEstilos.LogoAnchoCm, Unit.Centimetre).Image(logo).FitArea();
                else
                    izq.Item().Text(string.IsNullOrWhiteSpace(oc.Empresa.NombreComercial)
                        ? oc.Empresa.RazonSocial : oc.Empresa.NombreComercial).FontSize(PdfEstilos.Titulo).Bold();
                izq.Item().Text(oc.Empresa.RazonSocial).Bold();
                foreach (var linea in oc.Empresa.DomicilioLineas.Where(x => !string.IsNullOrWhiteSpace(x)))
                    izq.Item().Text(linea);
            });
            fila.ConstantItem(11, Unit.Centimetre).Column(der =>
            {
                if (!string.IsNullOrWhiteSpace(marca))
                    der.Item().AlignRight().Border(1).BorderColor(PdfEstilos.RojoMillet)
                        .Padding(4).Text(marca).Bold().FontColor(PdfEstilos.RojoMillet);
                der.Item().Background(PdfEstilos.GrisBarra).Padding(4).AlignRight()
                    .Text("ORDEN DE COMPRA").FontSize(PdfEstilos.Titulo).Bold().FontColor(PdfEstilos.Blanco);
                der.Item().Border(0.5f).BorderColor(PdfEstilos.GrisBarra).Padding(4).Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });
                    Dato(t, "Su referencia", oc.Referencia);
                    Dato(t, "Fecha de documento", Fecha(oc.FechaDocumento));
                    Dato(t, "Fecha de entrega", oc.FechaEntrega is { } e ? Fecha(e) : null);
                    Dato(t, "Número de documento", oc.Folio);
                    t.Cell().Column(c =>
                    {
                        c.Item().Text("Página").Bold().FontSize(PdfEstilos.Detalle);
                        c.Item().Text(x => { x.CurrentPageNumber(); x.Span("/"); x.TotalPages(); });
                    });
                });
            });
        });
    }

    private static void Dato(TableDescriptor tabla, string etiqueta, string? valor) =>
        tabla.Cell().PaddingBottom(3).Column(c =>
        {
            c.Item().Text(etiqueta).Bold().FontSize(PdfEstilos.Detalle);
            c.Item().Text(valor ?? "");
        });

    private static void Bloques(IContainer container, OrdenCompraImpresion oc)
    {
        container.Row(fila =>
        {
            fila.Spacing(12);
            fila.RelativeItem().Column(izq =>
            {
                izq.Spacing(6);
                izq.Item().Column(prov =>
                {
                    prov.Item().Text(oc.Proveedor.RazonSocial).Bold();
                    foreach (var linea in oc.Proveedor.DomicilioLineas.Where(x => !string.IsNullOrWhiteSpace(x)))
                        prov.Item().Text(linea);
                    if (!string.IsNullOrWhiteSpace(oc.Proveedor.TaxId))
                        prov.Item().Text($"Tax ID: {oc.Proveedor.TaxId}");
                    if (!string.IsNullOrWhiteSpace(oc.Incoterm))
                        prov.Item().Text($"INCOTERM: {oc.Incoterm}");
                });
                izq.Item().Component(new BloqueEtiquetado("Características de factura",
                [
                    new("Uso de CFDI:", oc.UsoCfdi),
                    new("Método de pago:", oc.MetodoPago),
                    new("Forma de pago:", oc.FormaPago),
                ]));
            });
            fila.RelativeItem().Column(der =>
            {
                der.Spacing(6);
                der.Item().Component(new BloqueEtiquetado("Proveedor",
                [
                    new("Número de proveedor", oc.Proveedor.Clave),
                    new("RFC", oc.Proveedor.Rfc),
                ]));
                der.Item().Component(new BloqueEtiquetado("Su contacto",
                [
                    new("", oc.Contacto?.Nombre),
                    new("Teléfono:", oc.Contacto?.Telefono),
                    new("Email:", oc.Contacto?.Email),
                ]));
                der.Item().Component(new BloqueEtiquetado("Dirección de entrega",
                    new CampoEtiquetado[] { new("", oc.EntregaNombre) }
                        .Concat(oc.EntregaLineas.Select(l => new CampoEtiquetado("", l)))));
            });
        });
    }

    private static void Renglones(IContainer container, OrdenCompraImpresion oc) =>
        container.Component(new TablaRenglones<RenglonImpresion>(oc.Renglones,
        [
            new("Clave", r => r.Clave, 1.3f),
            new("Descripción", r => r.Descripcion, 3f),
            new("Comentario", r => r.Comentario, 2.6f),
            new("Cantidad", r => r.Cantidad.ToString("0.####", Mx), 1f, AlineacionColumna.Derecha),
            new("U. medida", r => r.UnidadMedida, 1f, AlineacionColumna.Centro),
            new("Precio", r => r.PrecioUnitario.ToString("N4", Mx), 1.3f, AlineacionColumna.Derecha),
            new("Impuesto %", r => r.ImpuestoPorcentaje.ToString("0.00##", Mx), 1.2f, AlineacionColumna.Derecha),
            new("Total", r => r.Total.ToString("N2", Mx), 1.4f, AlineacionColumna.Derecha),
        ]));

    private static void Totales(IContainer container, OrdenCompraImpresion oc)
    {
        var renglones = new List<RenglonTotal> { new("Subtotal del pedido", oc.Subtotal, oc.Moneda) };
        if (oc.Descuento > 0m)
            renglones.Add(new("Descuento", -oc.Descuento, oc.Moneda));
        renglones.Add(new("Total antes del impuesto", oc.BaseGravable, oc.Moneda));
        renglones.Add(new("Importe total del impuesto", oc.Impuesto, oc.Moneda));
        if (oc.Retenciones > 0m)
            renglones.Add(new("Retenciones", -oc.Retenciones, oc.Moneda));
        renglones.Add(new("Importe total", oc.Total, oc.Moneda, Resaltado: true));

        container.Row(fila =>
        {
            fila.Spacing(12);
            fila.RelativeItem().Text(t =>
            {
                if (string.IsNullOrWhiteSpace(oc.CondicionPago)) return;
                t.Span("Condición de pago: ").Bold();
                t.Span(oc.CondicionPago);
            });
            fila.RelativeItem().Component(new BloqueTotales(new Millet.Documentos.Totales(renglones)));
        });
    }

    private static void Firma(IContainer container, string etiqueta, FirmaImpresion firma) =>
        container.Column(c =>
        {
            c.Item().Text(etiqueta).Bold();
            c.Item().PaddingTop(18).BorderTop(0.5f).BorderColor(PdfEstilos.GrisBarra).PaddingTop(2)
                .Text(firma.Nombre ?? "");
            c.Item().Text(firma.Fecha is { } f ? Fecha(f) : "").FontSize(PdfEstilos.Detalle);
        });

    private static string Fecha(DateOnly fecha) => fecha.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
}
