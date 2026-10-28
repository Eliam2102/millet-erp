using System.Globalization;
using System.Reflection;
using System.Text;
using Millet.Documentos.Componentes;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Millet.Documentos.UnitTests;

public sealed class DocumentoMuestraTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Should_Render_AllComponentsAcrossPages(bool conLogo)
    {
        PdfLicencia.Inicializar();
        var documento = Crear(conLogo);
        var pdf = documento.GeneratePdf();
        Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
        // Cada imagen representa una página realmente compuesta por QuestPDF.
        var paginas = documento.GenerateImages(new ImageGenerationSettings { RasterDpi = 72 }).ToArray();
        // Con Carlito incrustado la paginación es la misma en la Mac, en el CI y en Azure (Linux):
        // 60 renglones y los bloques del documento ocupan exactamente tres páginas.
        paginas.Length.Should().Be(3);
        paginas.Should().OnlyContain(x => x.Length > 1000);
        var salida = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "../../../Salidas"));
        Directory.CreateDirectory(salida);
        var nombre = conLogo ? "muestra-kit" : "muestra-kit-sin-logo";
        File.WriteAllBytes(Path.Combine(salida, nombre + ".pdf"), pdf);
        for (var i = 0; i < paginas.Length; i++)
            File.WriteAllBytes(Path.Combine(salida, $"{nombre}-{i + 1}.png"), paginas[i]);
    }

    private static Document Crear(bool conLogo)
    {
        byte[]? logo = null;
        if (conLogo)
        {
            using var recurso = Assembly.GetExecutingAssembly().GetManifestResourceStream("Millet.Documentos.UnitTests.Recursos.logo-millet.jpg")!;
            using var memoria = new MemoryStream();
            recurso.CopyTo(memoria);
            logo = memoria.ToArray();
        }

        var empresa = new EmpresaEncabezado("M INDUSTRIA", "MILLET", "MIN120224147",
            "601-General de Ley Personas Morales", ["Cancún, Quintana Roo, México"], logo);
        var titulo = new DocumentoTitulo("FACTURA DE EJEMPLO", "SAMPLE INVOICE", "DEMO 0001",
            new DateTime(2026, 10, 20), "BORRADOR – SIN VALIDEZ FISCAL");
        var url = CfdiQr.Url("00000000-0000-0000-0000-000000000001", empresa.Rfc, "XAXX010101000", 2898.04m, "SELLOFICTICIO12345678");

        return Document.Create(documento => documento.Page(pagina =>
        {
            PdfEstilos.ConfigurarPagina(pagina);
            pagina.Header().PaddingBottom(8).Component(new EncabezadoEmpresa(empresa, titulo));
            pagina.Content().Column(contenido =>
            {
                contenido.Spacing(6);
                contenido.Item().Row(fila =>
                {
                    fila.Spacing(8);
                    fila.RelativeItem().Component(new BloqueEtiquetado("CLIENTE/CUSTOMER:",
                    [new("Nombre:", "CLIENTE FICTICIO PARA PRUEBAS"), new("RFC:", "XAXX010101000"), new("Campo vacío:", " ")]));
                    fila.RelativeItem().Component(new BloqueEtiquetado("CFDI:",
                    [new("Uso:", "G03-Gastos en general"), new("Datos:", "Ejemplo ficticio; no representa una operación real.")]));
                });
                contenido.Item().DefaultTextStyle(x => x.FontSize(PdfEstilos.Detalle)).Component(new TablaRenglones<int>(Enumerable.Range(1, 60),
                [new("Partida", x => x.ToString(CultureInfo.InvariantCulture), 1, EncabezadoEn: "Item"),
                 new("Descripción", x => $"ARTÍCULO FICTICIO {x:00}", 4, EncabezadoEn: "Description"),
                 new("Cantidad", _ => "1", 1, AlineacionColumna.Centro, "Quantity"),
                 new("Importe", x => x == 60 ? "41.55 MXN" : "41.64 MXN", 2, AlineacionColumna.Derecha, "Amount")],
                    x => x % 10 == 0 ? $"ITEM.{x} REF: REFERENCIA FICTICIA DE PRUEBA" : null));
                contenido.Item().Component(new BloqueTotales(new Totales(
                [new("Subtotal", 2498.31m, "MXN"), new("IVA", 399.73m, "MXN"), new("Total", 2898.04m, "MXN", true)])));
                contenido.Item().Text(ImporteConLetra.Espanol(2898.04m, "MXN")).FontSize(PdfEstilos.Detalle);
                contenido.Item().Text(ImporteConLetra.Ingles(2898.04m, "MXN")).FontSize(PdfEstilos.Detalle);
                contenido.Item().Component(new BloqueTimbre(url, "20/10/2026 12:00:00",
                    "SELLOFICTICIO" + new string('A', 344), "SELLOSATFICTICIO" + new string('B', 344),
                    "||CADENA ORIGINAL FICTICIA PARA PRUEBAS|" + new string('C', 500) + "||"));
            });
            pagina.Footer().Component(new PiePagina("Este documento es una representación impresa de un CFDI"));
        }));
    }
}
