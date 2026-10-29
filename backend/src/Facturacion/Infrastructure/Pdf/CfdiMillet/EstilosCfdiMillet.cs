namespace Millet.Facturacion.Infrastructure.Pdf.CfdiMillet;

/// <summary>
/// Tokens del formato CFDI de Millet (FMT-4), medidos sobre los originales FEXP 758332,
/// FANT 504596 y NCRED 1079125: barras gris claro con texto negro, leyenda azul y
/// tamaños en puntos. Únicos colores y medidas de <see cref="CfdiMilletDocument"/>.
/// </summary>
internal static class EstilosCfdiMillet
{
    public const string Negro = "#000000";
    public const string Gris = "#BFBFBF";
    public const string Borde = "#A6A6A6";
    public const string AzulLeyenda = "#0000FF";

    public const float MargenPulgadas = 0.45f;
    public const float Linea = 1f;

    public const float Titulo = 11;
    public const float Empresa = 9.5f;
    public const float Fecha = 10;
    public const float Seccion = 9;
    public const float Texto = 8;
    public const float Detalle = 7;
    public const float Columna = 6.5f;
    public const float Total = 9;
    public const float Sello = 6.5f;
    public const float Leyenda = 10;
    public const float Pie = 7;

    public const float LogoAnchoCm = 5.2f;
    public const float TotalesAnchoCm = 7.2f;
    public const float QrColumnaCm = 4.2f;
    public const float QrCm = 3.4f;
}
