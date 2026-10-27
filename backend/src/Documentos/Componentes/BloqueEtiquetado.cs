using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Millet.Documentos.Componentes;

public sealed record CampoEtiquetado(string Etiqueta, string? Valor);

public sealed class BloqueEtiquetado(string titulo, IEnumerable<CampoEtiquetado> campos) : IComponent
{
    private readonly CampoEtiquetado[] _campos = campos.ToArray();

    public void Compose(IContainer container)
    {
        container.Border(0.5f).BorderColor(PdfEstilos.GrisBarra).Column(columna =>
        {
            columna.Item().Element(PdfEstilos.Barra).Text(titulo);
            columna.Item().Padding(5).Column(contenido =>
            {
                foreach (var campo in _campos.Where(x => !string.IsNullOrWhiteSpace(x.Valor)))
                    contenido.Item().Text(texto =>
                    {
                        texto.Span(campo.Etiqueta + " ").Bold();
                        texto.Span(campo.Valor);
                    });
            });
        });
    }
}
