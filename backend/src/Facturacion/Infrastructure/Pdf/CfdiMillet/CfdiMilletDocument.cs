using System.Globalization;
using Millet.Documentos;
using Millet.Facturacion.Domain.Ports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Millet.Facturacion.Infrastructure.Pdf.CfdiMillet;

/// <summary>
/// Representación impresa del CFDI con el formato de Millet (FMT-4, 86e3ukucq):
/// factura, factura de anticipo y nota de crédito comparten el diseño de los
/// originales FEXP 758332, FANT 504596 y NCRED 1079125. Carta, bilingüe ES/EN,
/// con la fuente Carlito del kit (FMT-0b). Solo presenta el <see cref="CfdiPdfModel"/>.
/// </summary>
public sealed class CfdiMilletDocument(CfdiPdfModel m) : IDocument
{
    private const string Leyenda = "\"Este documento es una representación impresa de un CFDI\"";
    private static readonly CultureInfo Numeros = CultureInfo.InvariantCulture;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = Titulo,
        Author = m.Emisor.RazonSocial,
        Creator = "Millet ERP",
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            PdfEstilos.ConfigurarPagina(page);
            page.Margin(EstilosCfdiMillet.MargenPulgadas, Unit.Inch);
            page.DefaultTextStyle(x => x.FontFamily(PdfFuentes.Familia).FontSize(EstilosCfdiMillet.Texto).FontColor(EstilosCfdiMillet.Negro));

            page.Header().Column(encabezado =>
            {
                encabezado.Item().ShowOnce().Element(EncabezadoCompleto);
                encabezado.Item().SkipOnce().Element(EncabezadoCompacto);
            });

            page.Content().PaddingTop(8).Column(col =>
            {
                col.Item().Element(ClienteYCfdi);
                col.Item().Element(TablaConceptos);
                col.Item().PaddingTop(4).Element(TotalesFisicosEImpuestos);
                col.Item().PaddingTop(6).Element(ComentariosLetraYTotales);
                col.Item().PaddingTop(8).Element(DatosDePago);
                if (m.Relacionados.Count > 0)
                    col.Item().PaddingTop(8).Element(Relacionados);
                if (m.Timbre is not null)
                    col.Item().PaddingTop(8).Element(Timbre);
            });

            page.Footer().Row(fila =>
            {
                fila.ConstantItem(80);
                fila.RelativeItem().AlignCenter().Text(Leyenda).Bold().Italic()
                    .FontSize(EstilosCfdiMillet.Leyenda).FontColor(EstilosCfdiMillet.AzulLeyenda);
                fila.ConstantItem(80).AlignRight().AlignBottom().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(EstilosCfdiMillet.Pie));
                    t.Span("Página ");
                    t.CurrentPageNumber();
                    t.Span(" de ");
                    t.TotalPages();
                });
            });
        });
    }

    /// <summary>«FACTURA/INVOICE:FEXP 758332», «NOTA DE CRÉDITO/CREDIT MEMO:NCRED 1079125».</summary>
    internal string Titulo => $"{m.TituloEs}/{m.TituloEn}:{SerieFolio}";

    private string SerieFolio => string.IsNullOrEmpty(m.Serie) ? m.Folio : $"{m.Serie} {m.Folio}";

    private void EncabezadoCompleto(IContainer container)
    {
        container.Row(fila =>
        {
            var izquierda = fila.ConstantItem(EstilosCfdiMillet.LogoAnchoCm, Unit.Centimetre).PaddingTop(14);
            if (m.Emisor.Logo is { Length: > 0 } logo)
                izquierda.MaxHeight(2.4f, Unit.Centimetre).Image(logo).FitArea();
            else
                izquierda.Text(m.Emisor.NombreComercial ?? m.Emisor.RazonSocial).FontSize(16).Bold();

            fila.RelativeItem().AlignRight().Column(derecha =>
            {
                derecha.Item().AlignRight().Text(Titulo).Bold().FontSize(EstilosCfdiMillet.Titulo);
                if (m.Cabecera.Uuid is null)
                    derecha.Item().AlignRight().Text("SIN TIMBRAR – SIN VALOR FISCAL").Bold().FontSize(EstilosCfdiMillet.Texto);
                derecha.Item().PaddingTop(8).AlignRight().Text(m.Emisor.RazonSocial).Bold().FontSize(EstilosCfdiMillet.Empresa);
                derecha.Item().PaddingTop(8).Column(datos =>
                {
                    datos.Spacing(2);
                    foreach (var linea in m.Emisor.DomicilioLineas)
                        datos.Item().AlignRight().Text(linea);
                    datos.Item().AlignRight().Text($"RFC:{m.Emisor.Rfc}");
                    datos.Item().AlignRight().Text($"Régimen Fiscal/Fiscal Scheme: {m.Emisor.Regimen.Replace(" - ", "-")}");
                });
                derecha.Item().AlignRight().Text(m.FechaEmision.ToString("dd/MM/yyyy", Numeros)).FontSize(EstilosCfdiMillet.Fecha);
            });
        });
    }

    /// <summary>Desde la página 2, como el original: razón social y título con una línea debajo.</summary>
    private void EncabezadoCompacto(IContainer container)
    {
        container.BorderBottom(0.75f).BorderColor(EstilosCfdiMillet.Borde).PaddingBottom(4).Row(fila =>
        {
            fila.RelativeItem().Text(m.Emisor.RazonSocial).Bold().FontSize(EstilosCfdiMillet.Empresa);
            fila.RelativeItem().AlignRight().Text(Titulo).Bold().FontSize(EstilosCfdiMillet.Empresa);
        });
    }

    private void ClienteYCfdi(IContainer container)
    {
        container.Background(EstilosCfdiMillet.Gris).Padding(3).Column(col =>
        {
            col.Item().Row(titulos =>
            {
                titulos.Spacing(4);
                titulos.RelativeItem().AlignCenter().PaddingVertical(3).Text("CLIENTE/CUSTOMER:").Bold().FontSize(EstilosCfdiMillet.Seccion);
                titulos.RelativeItem().AlignCenter().PaddingVertical(3).Text("CFDI:").Bold().FontSize(EstilosCfdiMillet.Seccion);
            });
            col.Item().Row(cajas =>
            {
                cajas.Spacing(4);
                cajas.RelativeItem().Background(Colors.White).Padding(6).Column(cliente =>
                {
                    cliente.Spacing(2);
                    cliente.Item().Text(m.Receptor.Nombre).Bold();
                    cliente.Item().Text($"RFC:{m.Receptor.Rfc}").Bold();
                    cliente.Item().PaddingTop(4).Text($"DomicilioFiscalReceptor:{m.Receptor.CodigoPostalFiscal}").Bold().FontSize(EstilosCfdiMillet.Detalle);
                    cliente.Item().Text($"RégimenFiscalReceptor:{m.Receptor.Regimen}").Bold().FontSize(EstilosCfdiMillet.Detalle);
                    if (m.Receptor.Pais != "MEX")
                        cliente.Item().Text(m.Receptor.Pais).FontSize(EstilosCfdiMillet.Detalle);
                    cliente.Item().PaddingTop(6).Text(t =>
                    {
                        t.Span("Condiciones de pago/Payment terms").Bold();
                        t.Span($": {m.Receptor.CondicionesPago}").FontSize(EstilosCfdiMillet.Detalle);
                    });
                });
                cajas.RelativeItem().Background(Colors.White).PaddingVertical(4).PaddingHorizontal(6).Table(tabla =>
                {
                    tabla.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(1.35f);
                        c.RelativeColumn();
                    });
                    foreach (var (etiqueta, valor) in FilasCfdi(m))
                    {
                        tabla.Cell().PaddingVertical(2).AlignRight().Text(etiqueta).Bold().FontSize(EstilosCfdiMillet.Detalle);
                        tabla.Cell().PaddingVertical(2).PaddingLeft(4).AlignCenter().Text(valor ?? "").FontSize(EstilosCfdiMillet.Detalle);
                    }
                });
            });
        });
    }

    /// <summary>Renglones del recuadro CFDI: siempre los siete, aunque el valor vaya vacío (como el original).</summary>
    internal static IReadOnlyList<(string Etiqueta, string? Valor)> FilasCfdi(CfdiPdfModel modelo) =>
    [
        ("N° Orden de compra/Purchase order:", modelo.Cabecera.OrdenCompraCliente),
        ("N° Referencia/Reference", modelo.Cabecera.Referencia),
        ("Certificado CSD/CSD Certificate:", modelo.Cabecera.NoCertificadoEmisor),
        ("Certificado del SAT/Sat Certificate Serial Number:", modelo.Cabecera.NoCertificadoSat),
        ("Fecha de Timbrado/Certification date:", modelo.Cabecera.FechaTimbrado?.ToString("yyyy-MM-ddTHH:mm:ss", Numeros)),
        ("Folio Fiscal/Invoice id (uuid):", modelo.Cabecera.Uuid?.ToUpperInvariant()),
        ("Tipo Comprobante:", modelo.TipoComprobante),
    ];

    private void TablaConceptos(IContainer container)
    {
        container.Table(tabla =>
        {
            tabla.ColumnsDefinition(c =>
            {
                c.RelativeColumn(1.7f);
                c.RelativeColumn(3.6f);
                c.RelativeColumn(0.9f);
                c.RelativeColumn(0.8f);
                c.RelativeColumn(1);
                c.RelativeColumn(1);
                c.RelativeColumn(0.9f);
            });
            tabla.Header(h =>
            {
                Encabezado(h.Cell(), "Codigo/ClavProdServ", "SAT KEY", Alineacion.Izquierda);
                Encabezado(h.Cell(), "DESCRIPCIÓN", "DESCRIPTION", Alineacion.Centro);
                Encabezado(h.Cell(), "CANTIDAD", "QUANTITY", Alineacion.Centro);
                Encabezado(h.Cell(), "CVE UNIDAD", "KEY UNIT", Alineacion.Centro);
                Encabezado(h.Cell(), "P.UNITARIO", "UNIT PRICE", Alineacion.Centro);
                Encabezado(h.Cell(), "DESCUENTO", "DISCOUNT", Alineacion.Centro);
                Encabezado(h.Cell(), "IMPORTE", "AMOUNT", Alineacion.Derecha);
            });
            foreach (var c in m.Conceptos)
            {
                tabla.Cell().Element(Renglon).Text(Codigo(c));
                tabla.Cell().Element(Renglon).AlignCenter().Column(d =>
                {
                    d.Item().AlignCenter().Text(c.Descripcion);
                    if (!string.IsNullOrWhiteSpace(c.LineaReferencia))
                        d.Item().AlignCenter().Text(c.LineaReferencia);
                });
                tabla.Cell().Element(Renglon).AlignCenter().Text(Importe(c.Cantidad));
                tabla.Cell().Element(Renglon).AlignCenter().Text(c.ClaveUnidad);
                tabla.Cell().Element(Renglon).AlignCenter().Text(Importe(c.ValorUnitario));
                tabla.Cell().Element(Renglon).AlignCenter().Text(Importe(c.Descuento));
                tabla.Cell().Element(Renglon).AlignRight().Text(Importe(c.Importe));
            }
        });
    }

    private void TotalesFisicosEImpuestos(IContainer container)
    {
        var t = m.TotalesFisicos;
        container.Row(fila =>
        {
            fila.RelativeItem(1.15f).AlignBottom().Row(medidas =>
            {
                medidas.RelativeItem().Text($"Total Kg.:{(t.Kg is null ? "" : Medida(t.Kg.Value))}");
                medidas.RelativeItem().Text($"Total Pie².:{(t.Pie2 is null ? "" : Medida(t.Pie2.Value))}");
                medidas.RelativeItem().Text($"Total Qty.:{t.Piezas.ToString("#,##0.##", Numeros)}");
            });
            fila.RelativeItem().Table(tabla =>
            {
                tabla.ColumnsDefinition(c =>
                {
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                });
                tabla.Cell().AlignCenter().Text("Tipo Impuesto");
                tabla.Cell().AlignCenter().Text("TasaOCuota");
                tabla.Cell().AlignCenter().Text("TipoFactor");
                tabla.Cell().AlignRight().Text("Importe");
                foreach (var i in m.Impuestos)
                {
                    tabla.Cell().PaddingTop(3).AlignCenter().Text(i.EsRetencion ? $"{i.Nombre} Ret." : i.Nombre);
                    tabla.Cell().PaddingTop(3).AlignCenter().Text(i.TasaOCuota.ToString("0.000000", Numeros));
                    tabla.Cell().PaddingTop(3).AlignCenter().Text(i.TipoFactor);
                    tabla.Cell().PaddingTop(3).AlignRight().Text(Importe(i.Importe));
                }
            });
        });
    }

    private void ComentariosLetraYTotales(IContainer container)
    {
        container.Row(fila =>
        {
            fila.Spacing(10);
            fila.RelativeItem().Column(izquierda =>
            {
                izquierda.Item().Border(EstilosCfdiMillet.Linea).BorderColor(EstilosCfdiMillet.Borde).MinHeight(62).Padding(3).Text(t =>
                {
                    t.Span("Comentarios/Comments:");
                    t.Span(m.Comentarios ?? "");
                });
                izquierda.Item().PaddingTop(6).Border(EstilosCfdiMillet.Linea).BorderColor(EstilosCfdiMillet.Borde).Column(letra =>
                {
                    letra.Item().Background(EstilosCfdiMillet.Gris).PaddingVertical(2).PaddingHorizontal(6)
                        .Text("MONTO EN LETRA/AMOUNT IN LETTER:").Bold().FontSize(EstilosCfdiMillet.Seccion);
                    letra.Item().PaddingHorizontal(6).PaddingVertical(2).Text(MontoEnLetra);
                });
            });
            fila.ConstantItem(EstilosCfdiMillet.TotalesAnchoCm, Unit.Centimetre).Border(EstilosCfdiMillet.Linea)
                .BorderColor(EstilosCfdiMillet.Borde).Background(EstilosCfdiMillet.Gris).Padding(3).Column(totales =>
                {
                    totales.Spacing(3);
                    var tot = m.Totales;
                    RenglonTotal(totales.Item(), "Subtotal:", tot.Subtotal, tot.Moneda, false);
                    RenglonTotal(totales.Item(), "Descuento/Discount:", tot.Descuento, tot.Moneda, false);
                    RenglonTotal(totales.Item(), "Impuesto/Tax:", tot.Impuestos, tot.Moneda, false);
                    if (tot.Retenciones != 0)
                        RenglonTotal(totales.Item(), "Retenciones/Withholdings:", tot.Retenciones, tot.Moneda, false);
                    RenglonTotal(totales.Item(), "Total:", tot.Total, tot.Moneda, true);
                });
        });
    }

    private void DatosDePago(IContainer container)
    {
        container.DefaultTextStyle(x => x.FontSize(EstilosCfdiMillet.Detalle)).Row(fila =>
        {
            fila.Spacing(12);
            Rotulado(fila.RelativeItem(), "Uso de CFDI/CFDI Usage:", m.Pago.UsoCfdi);
            Rotulado(fila.RelativeItem(), "Método de pago/Payment method:", m.Pago.MetodoPago);
            Rotulado(fila.RelativeItem(), "Forma de Pago/Way of Payment :", m.Pago.FormaPago);
        });
    }

    private void Relacionados(IContainer container)
    {
        container.DefaultTextStyle(x => x.FontSize(EstilosCfdiMillet.Detalle)).Table(tabla =>
        {
            tabla.ColumnsDefinition(c =>
            {
                c.ConstantColumn(7.6f, Unit.Centimetre);
                c.RelativeColumn();
            });
            tabla.Cell().PaddingBottom(4).Text("Cfdi Relacionado UUID");
            tabla.Cell().PaddingBottom(4).Text("TipoRelacionCFDI");
            foreach (var r in m.Relacionados)
            {
                tabla.Cell().PaddingBottom(2).Text(r.Uuid.ToUpperInvariant());
                tabla.Cell().PaddingBottom(2).Text(r.TipoRelacion);
            }
        });
    }

    private void Timbre(IContainer container)
    {
        var timbre = m.Timbre!;
        var fecha = m.Cabecera.FechaTimbrado?.ToString("yyyy-MM-ddTHH:mm:ss", Numeros) ?? "";
        container.ShowEntire().Column(col =>
        {
            Sello(col.Item(), "Sello Digital del CFDI/CFDI DIGITAL SIGNATURE:", timbre.SelloCfdi);
            col.Item().PaddingTop(4).Row(fila =>
            {
                fila.Spacing(6);
                fila.ConstantItem(EstilosCfdiMillet.QrColumnaCm, Unit.Centimetre).Column(qr =>
                {
                    if (timbre.UrlQr is not null)
                        qr.Item().AlignCenter().Width(EstilosCfdiMillet.QrCm, Unit.Centimetre)
                            .Height(EstilosCfdiMillet.QrCm, Unit.Centimetre).Image(CfdiQr.Png(timbre.UrlQr)).FitArea();
                    qr.Item().PaddingTop(4).AlignCenter().Text($"Fecha de Timbrado:\n{fecha}").AlignCenter().FontSize(EstilosCfdiMillet.Detalle);
                });
                fila.RelativeItem().Column(derecha =>
                {
                    Sello(derecha.Item(), "Sello Digital del SAT/SAT  DIGITAL SIGNATURE:", timbre.SelloSat);
                    Sello(derecha.Item(), "Cadena Original del Complemento de Certificación Digital del SAT/ORIGINAL SAT COMPLEMENT CERTIFICATION:", timbre.CadenaOriginal);
                });
            });
        });
    }

    private static void Sello(IContainer container, string titulo, string valor)
    {
        container.Border(EstilosCfdiMillet.Linea).BorderColor(EstilosCfdiMillet.Borde).Column(col =>
        {
            col.Item().Background(EstilosCfdiMillet.Gris).PaddingVertical(2).PaddingHorizontal(6)
                .Text(titulo).Bold().Italic().FontSize(EstilosCfdiMillet.Detalle);
            col.Item().PaddingVertical(4).PaddingHorizontal(6).Text(valor).FontSize(EstilosCfdiMillet.Sello);
        });
    }

    private static void RenglonTotal(IContainer container, string etiqueta, decimal importe, string moneda, bool resaltado)
    {
        container.Background(Colors.White).PaddingVertical(4).PaddingHorizontal(6).Row(fila =>
        {
            fila.RelativeItem().AlignMiddle().Text(etiqueta).Bold().FontSize(EstilosCfdiMillet.Total);
            var valor = fila.RelativeItem().AlignRight().AlignMiddle().Text($"{Importe(importe)} {moneda}");
            if (resaltado) valor.Bold();
        });
    }

    private static void Rotulado(IContainer container, string rotulo, string valor) =>
        container.Text(t =>
        {
            t.Span(rotulo).Bold();
            t.Span(valor);
        });

    /// <summary>Encabezado de columna gris claro con texto negro en dos líneas ES/EN (FEXP, FANT y NCRED).</summary>
    private static void Encabezado(IContainer celda, string es, string en, Alineacion alineacion)
    {
        var c = celda.Background(EstilosCfdiMillet.Gris).PaddingVertical(2).PaddingHorizontal(1)
            .DefaultTextStyle(x => x.FontSize(EstilosCfdiMillet.Columna).Bold());
        c.Column(col =>
        {
            col.Item().Element(x => Alinear(x, alineacion)).Text(es);
            col.Item().Element(x => Alinear(x, alineacion)).Text(en);
        });
    }

    private static IContainer Alinear(IContainer x, Alineacion alineacion) => alineacion switch
    {
        Alineacion.Centro => x.AlignCenter(),
        Alineacion.Derecha => x.AlignRight(),
        _ => x.AlignLeft(),
    };

    private static IContainer Renglon(IContainer celda) => celda.PaddingTop(5).PaddingHorizontal(1);

    private enum Alineacion { Izquierda, Centro, Derecha }

    private string MontoEnLetra => string.IsNullOrWhiteSpace(m.MontoEnLetra.Es)
        ? ""
        : $"{m.MontoEnLetra.Es} / {m.MontoEnLetra.En}";

    /// <summary>«código del artículo / clave SAT» (VIDB0002 / 30171710); sin artículo, solo la clave SAT.</summary>
    internal static string Codigo(ConceptoPdf c) =>
        string.IsNullOrWhiteSpace(c.CodigoArticulo) ? c.ClaveProdServ : $"{c.CodigoArticulo} / {c.ClaveProdServ}";

    private static string Importe(decimal v) => v.ToString("N2", Numeros);

    /// <summary>Kg y pie² como en el formato de Millet: siempre 2 decimales (15,295.76).</summary>
    internal static string Medida(decimal v) => v.ToString("N2", Numeros);
}
