namespace Millet.CuentasPorPagar.Domain.Cfdi;

/// <summary>
/// Puerto del parser de CFDI 4.0 (F1-PR1, A8 del 01-diseno §3). Vive en
/// Domain porque el contrato es estable (CFDI 4.0 es un estándar SAT
/// versionado); la implementación con <c>XmlReader</c> vive en
/// Infrastructure.
/// </summary>
public interface IXmlCfdiParser
{
    /// <summary>
    /// Parsea el contenido XML de un CFDI 4.0 y devuelve los datos
    /// extraídos. Lanza <see cref="CfdiParseException"/> si el XML está
    /// corrupto, no es CFDI 4.0, o no tiene <c>TimbreFiscalDigital</c>
    /// (sin timbrar no es un CFDI válido).
    /// </summary>
    DatosCfdiParseados Parsear(Stream xml);

    /// <summary>Overload que recibe el contenido como string (fixtures, tests).</summary>
    DatosCfdiParseados Parsear(string xml);
}

/// <summary>
/// Exception lanzada por el parser cuando el XML no es un CFDI 4.0
/// válido o le falta el timbrado (F1-PR1).
/// </summary>
public sealed class CfdiParseException : Exception
{
    public string Codigo { get; }

    public CfdiParseException(string codigo, string mensaje) : base(mensaje)
    {
        Codigo = codigo;
    }
}
