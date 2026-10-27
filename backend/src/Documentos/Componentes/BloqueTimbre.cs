using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Millet.Documentos.Componentes;

public sealed class BloqueTimbre(string urlQr, string fechaTimbrado, string selloCfdi,
    string selloSat, string cadenaOriginal) : IComponent
{
    private readonly QrCfdi _qr = new(urlQr);

    public void Compose(IContainer container)
    {
        container.ShowEntire().Row(fila =>
        {
            fila.Spacing(8);
            fila.ConstantItem(PdfEstilos.QrCm, Unit.Centimetre).Column(izquierda =>
            {
                izquierda.Item().Component(_qr);
                izquierda.Item().PaddingTop(4).Text("Fecha de timbrado\n" + fechaTimbrado).FontSize(PdfEstilos.Detalle);
            });
            fila.RelativeItem().Column(derecha =>
            {
                derecha.Spacing(5);
                Caja(derecha.Item(), "Sello Digital del CFDI/CFDI DIGITAL SIGNATURE", selloCfdi);
                Caja(derecha.Item(), "Sello Digital del SAT/SAT DIGITAL SIGNATURE", selloSat);
                Caja(derecha.Item(), "Cadena Original del Complemento de Certificación Digital del SAT/ORIGINAL SAT COMPLEMENT CERTIFICATION", cadenaOriginal);
            });
        });
    }

    private static void Caja(IContainer container, string titulo, string valor)
    {
        container.Border(0.5f).BorderColor(PdfEstilos.GrisBarra)
            .DefaultTextStyle(x => x.FontSize(PdfEstilos.Timbre)).Column(columna =>
            {
                columna.Item().Element(PdfEstilos.Barra).Text(titulo);
                columna.Item().Padding(4).Text(valor);
            });
    }
}
