using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Millet.Documentos.Componentes;

/// <summary>Colocar en page.Footer() para repetir la leyenda y numeración en todas las páginas.</summary>
public sealed class PiePagina(string? leyenda = null) : IComponent
{
    public void Compose(IContainer container)
    {
        container.PaddingTop(5).DefaultTextStyle(x => x.FontSize(PdfEstilos.Detalle)).Row(fila =>
        {
            fila.ConstantItem(75);
            fila.RelativeItem().AlignCenter().Text(leyenda ?? "").FontColor(PdfEstilos.AzulMarca);
            fila.ConstantItem(75).AlignRight().Text(texto =>
            {
                texto.Span("Página ");
                texto.CurrentPageNumber();
                texto.Span(" de ");
                texto.TotalPages();
            });
        });
    }
}
