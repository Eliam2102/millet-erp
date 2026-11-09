namespace Millet.CuentasPorPagar.Infrastructure.GeneradorPrueba;

/// <summary>
/// Generador de XML CFDI 4.0 sintético para pruebas y demostraciones en ambientes no productivos.
/// </summary>
public interface IGeneradorCfdiPruebaService
{
    /// <summary>
    /// Genera la representación en string del XML CFDI 4.0 sintético.
    /// </summary>
    string GenerarXml(DatosGeneracionCfdiPrueba datos);

    /// <summary>
    /// Genera un Stream de memoria que contiene el XML CFDI 4.0 sintético listo para ingesta (posicionado en 0).
    /// </summary>
    Stream GenerarXmlStream(DatosGeneracionCfdiPrueba datos);
}
