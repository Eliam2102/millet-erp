using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Millet.Documentos.Componentes;

public sealed class BloqueTotales(Totales totales) : IComponent
{
    public void Compose(IContainer container)
    {
        container.AlignRight().MaxWidth(260).Border(0.5f).BorderColor(PdfEstilos.GrisBarra).Padding(5)
            .Column(columna =>
            {
                columna.Spacing(4);
                foreach (var renglon in totales.Renglones)
                    columna.Item().DefaultTextStyle(x => renglon.Resaltado ? x.Bold() : x).Row(fila =>
                    {
                        fila.RelativeItem().Text(renglon.Etiqueta);
                        fila.RelativeItem().AlignRight().Text($"{renglon.Importe.ToString("N2", CultureInfo.GetCultureInfo("es-MX"))} {renglon.Moneda}");
                    });
            });
    }
}
