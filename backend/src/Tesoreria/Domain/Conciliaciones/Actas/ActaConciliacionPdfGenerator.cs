using System.Globalization;
using Millet.Documentos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Millet.Tesoreria.Domain.Conciliaciones.Actas;

/// <summary>
/// Generador del Acta de Conciliación Bancaria en PDF (RN-7, R9, CA9.9).
/// Se emite únicamente cuando la conciliación está cuadrada con diferencia $0.00,
/// con la firma autorizada del Gerente o Jefe de Tesorería.
/// </summary>
public static class ActaConciliacionPdfGenerator
{
    private static readonly CultureInfo CulturaEs = CultureInfo.GetCultureInfo("es-MX");

    public static byte[] Generar(DatosActaConciliacion datos) => CrearDocumento(datos).GeneratePdf();

    public static Document CrearDocumento(DatosActaConciliacion d)
    {
        PdfLicencia.Inicializar();
        return Document.Create(doc =>
        {
            doc.Page(pagina =>
            {
            PdfEstilos.ConfigurarPagina(pagina);

            pagina.Header().Element(x => ConstruirEncabezado(x, d));

            pagina.Content().PaddingTop(12).Column(col =>
            {
                col.Spacing(12);

                // Tarjeta de información de la cuenta
                col.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(t => { t.Span("Institución Bancaria: ").Bold(); t.Span(d.Banco); });
                        c.Item().Text(t => { t.Span("Número de Cuenta: ").Bold(); t.Span(d.NumeroCuentaEnmascarado); });
                    });
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(t => { t.Span("Moneda: ").Bold(); t.Span(d.Moneda); });
                        c.Item().Text(t => { t.Span("Periodo Conciliado: ").Bold(); t.Span($"{ObtenerNombreMes(d.PeriodoMes)} {d.PeriodoAnio}"); });
                    });
                });

                // Cifras de control / Balances
                col.Item().Background(Colors.Grey.Lighten4).Padding(8).Column(c =>
                {
                    c.Item().Text("CIFRAS DE CONTROL Y CUADRE").Bold().FontSize(10).FontColor(PdfEstilos.AzulMarca);
                    c.Item().PaddingTop(4).Row(r =>
                    {
                        r.RelativeItem().Column(b =>
                        {
                            b.Item().Text("Saldo según Banco (Extracto):").FontSize(8).FontColor(Colors.Grey.Darken1);
                            b.Item().Text(d.SaldoExtracto.ToString("C2", CulturaEs)).Bold().FontSize(12);
                        });
                        r.RelativeItem().Column(b =>
                        {
                            b.Item().Text("Saldo según Libros (ERP):").FontSize(8).FontColor(Colors.Grey.Darken1);
                            b.Item().Text(d.SaldoLibros.ToString("C2", CulturaEs)).Bold().FontSize(12);
                        });
                        r.RelativeItem().Column(b =>
                        {
                            b.Item().Text("Diferencia Neta:").FontSize(8).FontColor(Colors.Grey.Darken1);
                            b.Item().Text(d.Diferencia.ToString("C2", CulturaEs))
                                .Bold().FontSize(12)
                                .FontColor(d.Diferencia == 0 ? Colors.Green.Darken2 : Colors.Red.Darken2);
                        });
                    });
                });

                // Resumen estadístico
                col.Item().Border(1).BorderColor(Colors.Grey.Lighten3).Padding(8).Row(r =>
                {
                    r.RelativeItem().Text(t => { t.Span("Movimientos extracto: ").Bold(); t.Span(d.TotalMovimientosExtracto.ToString()); });
                    r.RelativeItem().Text(t => { t.Span("Movimientos emparejados: ").Bold(); t.Span(d.TotalEmparejados.ToString()); });
                    r.RelativeItem().Text(t => { t.Span("Partidas en tránsito: ").Bold(); t.Span(d.PartidasTransito.Count.ToString()); });
                });

                // Tabla de partidas en tránsito si existen
                if (d.PartidasTransito.Count > 0)
                {
                    col.Item().Column(pt =>
                    {
                        pt.Item().Text("PARTIDAS EN TRÁNSITO AUTORIZADAS").Bold().FontSize(9).FontColor(PdfEstilos.AzulMarca);
                        pt.Item().Table(tabla =>
                        {
                            tabla.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(65);
                                cols.RelativeColumn(3);
                                cols.RelativeColumn(2);
                                cols.ConstantColumn(80);
                                cols.RelativeColumn(2);
                            });

                            tabla.Header(h =>
                            {
                                h.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text("Fecha").Bold().FontSize(8);
                                h.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text("Descripción").Bold().FontSize(8);
                                h.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text("Tipo").Bold().FontSize(8);
                                h.Cell().Background(Colors.Grey.Lighten3).Padding(3).AlignRight().Text("Importe").Bold().FontSize(8);
                                h.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text("Responsable").Bold().FontSize(8);
                            });

                            foreach (var p in d.PartidasTransito)
                            {
                                tabla.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(p.Fecha.ToString("dd/MM/yyyy")).FontSize(8);
                                tabla.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(p.Descripcion).FontSize(8);
                                tabla.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(p.Tipo).FontSize(8);
                                tabla.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3).AlignRight().Text(p.Monto.ToString("C2", CulturaEs)).FontSize(8);
                                tabla.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(p.Responsable).FontSize(8);
                            }
                        });
                    });
                }

                // Certificación y declaración de conformidad
                col.Item().PaddingTop(6).Border(1).BorderColor(Colors.Green.Lighten3).Background(Colors.Green.Lighten5).Padding(8).Column(cert =>
                {
                    cert.Item().Text("DECLARACIÓN DE CONFORMIDAD Y CIERRE").Bold().FontSize(8).FontColor(Colors.Green.Darken3);
                    cert.Item().Text(
                        "Se certifica que la presente conciliación bancaria mensual ha sido revisada a cabalidad, comprobándose " +
                        "que los saldos bancarios y los registros contables en libros de la empresa se encuentran completamente cuadrados " +
                        "con una diferencia neta de $0.00 (cero pesos), satisfaciendo los requisitos de control interno de Vidrios Millet.")
                        .FontSize(8).FontColor(Colors.Grey.Darken3);
                });

                // Firmas autorizadas
                col.Item().PaddingTop(24).Row(firmas =>
                {
                    firmas.Spacing(30);

                    firmas.RelativeItem().Column(f =>
                    {
                        f.Item().Height(40);
                        f.Item().BorderTop(1).BorderColor(Colors.Black).PaddingTop(4).AlignCenter().Text("Elaboró").Bold().FontSize(8);
                        f.Item().AlignCenter().Text("Departamento de Tesorería").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });

                    firmas.RelativeItem().Column(f =>
                    {
                        f.Item().Height(40);
                        f.Item().BorderTop(1).BorderColor(Colors.Black).PaddingTop(4).AlignCenter().Text(d.FirmanteNombre).Bold().FontSize(9);
                        f.Item().AlignCenter().Text(d.FirmantePuesto).FontSize(8);
                        f.Item().AlignCenter().Text($"Firmado el {d.FechaFirma:dd/MM/yyyy HH:mm}").FontSize(7).FontColor(Colors.Grey.Darken1);
                    });
                });
            });

            pagina.Footer().AlignCenter().Text(t =>
            {
                t.Span("Página ");
                t.CurrentPageNumber();
                t.Span(" de ");
                t.TotalPages();
                t.Span($" · Folio: {d.ConciliacionId} · Vidrios Millet");
            });
        });
    });
}

    private static void ConstruirEncabezado(IContainer container, DatosActaConciliacion d)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(c =>
            {
                c.Item().Text("VIDRIOS MILLET DE MÉXICO").Bold().FontSize(12).FontColor(PdfEstilos.AzulMarca);
                c.Item().Text($"RFC: {d.EmpresaRfc}").FontSize(8).FontColor(Colors.Grey.Darken1);
                c.Item().Text("MÓDULO DE TESORERÍA / BANCOS").FontSize(8).FontColor(Colors.Grey.Darken1);
            });

            row.RelativeItem().AlignRight().Column(c =>
            {
                c.Item().Text("ACTA DE CONCILIACIÓN BANCARIA").Bold().FontSize(12).FontColor(PdfEstilos.RojoMillet);
                c.Item().Text($"Mes: {ObtenerNombreMes(d.PeriodoMes)} {d.PeriodoAnio}").FontSize(9).Bold();
                c.Item().Text($"Fecha de Cierre: {d.FechaFirma:dd/MM/yyyy}").FontSize(8);
            });
        });
    }

    private static string ObtenerNombreMes(int mes) => mes switch
    {
        1 => "Enero",
        2 => "Febrero",
        3 => "Marzo",
        4 => "Abril",
        5 => "Mayo",
        6 => "Junio",
        7 => "Julio",
        8 => "Agosto",
        9 => "Septiembre",
        10 => "Octubre",
        11 => "Noviembre",
        12 => "Diciembre",
        _ => $"Mes {mes}",
    };
}
