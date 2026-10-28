using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Millet.Documentos;

public static class PdfEstilos
{
    public const string GrisBarra = "#B3B3B3";
    public const string RojoMillet = "#C8102E";
    public const string AzulMarca = "#003B71";
    public const string Blanco = "#FFFFFF";
    public const float MargenCm = 1.2f;
    public const float Texto = 9;
    public const float Titulo = 14;
    public const float Detalle = 7;
    public const float Timbre = 6;
    public const float LogoAnchoCm = 4.5f;
    public const float QrCm = 2.75f;
    public const float EspaciadoCelda = 2;

    public static void ConfigurarPagina(PageDescriptor pagina)
    {
        PdfFuentes.Registrar();
        pagina.Size(PageSizes.Letter);
        pagina.Margin(MargenCm, Unit.Centimetre);
        pagina.DefaultTextStyle(x => x.FontFamily(PdfFuentes.Familia).FontSize(Texto));
    }

    internal static IContainer Barra(IContainer container) => container
        .Background(GrisBarra).Padding(4)
        .DefaultTextStyle(x => x.FontColor(Blanco).Bold());
}
