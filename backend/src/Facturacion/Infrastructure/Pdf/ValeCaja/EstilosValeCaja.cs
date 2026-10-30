namespace Millet.Facturacion.Infrastructure.Pdf.ValeCaja;

/// <summary>
/// Tokens del Vale de Caja (86e3ww6tw), medidos sobre el formato de Millet del
/// 28-oct; el título ocupa ~28% del ancho del recuadro, como en el formato (recuadros grises de línea fina, texto negro, tamaños en puntos).
/// Únicos colores y medidas de <see cref="ValeCajaDocument"/>.
/// </summary>
internal static class EstilosValeCaja
{
    public const string Negro = "#000000";
    public const string Borde = "#7F7F7F";

    public const float MargenLateralPulgadas = 1f;
    public const float MargenVerticalPulgadas = 0.75f;
    public const float Linea = 0.75f;

    public const float Titulo = 23;
    public const float Subtitulo = 10;
    public const float Empresa = 9.5f;
    public const float Texto = 12;
    public const float Firma = 9;
    public const float Leyenda = 7;
    public const float Casilla = 9;

    public const float EncabezadoAlto = 85;
    public const float ColumnaLogo = 3;
    public const float ColumnaTitulo = 7;
    public const float LogoAlto = 48;
    public const float LogoAncho = 120;

    public const float EspacioFolio = 14;
    public const float EspacioRecibi = 14;
    public const float EspacioFormaPago = 14;
    public const float EspacioFirmas = 80;
    public const float EspacioRecuadro = 34;

    public const float OtroAncho = 140;
    public const float OtroSeparacion = 4;
    public const float FirmaEtiqueta = 10;
    public const float FirmaAncho = 140;
    public const float FilaImpresionAlto = 26;
    public const float FilaFirmaAlto = 58;
}
