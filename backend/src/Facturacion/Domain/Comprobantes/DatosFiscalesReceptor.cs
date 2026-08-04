namespace Millet.Facturacion.Domain.Comprobantes;

/// <summary>
/// Snapshot de los datos fiscales del receptor al momento de emisión (§4.2
/// diseño). Inmutable. Soporta receptor nominal y RFC genérico (público en
/// general nacional <c>XAXX010101000</c> / extranjero <c>XEXX010101000</c>, D4).
///
/// <para>
/// Es un objeto-parámetro del factory de <see cref="Comprobante"/>; sus campos
/// se persisten como columnas planas en la tabla base <c>comprobante</c> (mismo
/// criterio que CxP — sin owned types), no como tabla aparte.
/// </para>
/// </summary>
public sealed record DatosFiscalesReceptor(
    string Rfc,
    string Nombre,
    string RegimenFiscal,
    string CodigoPostal,
    string UsoCfdi,
    string Pais,
    bool EsGenerico)
{
    /// <summary>RFC genérico nacional (público en general).</summary>
    public const string RfcGenericoNacional = "XAXX010101000";

    /// <summary>RFC genérico de extranjero.</summary>
    public const string RfcGenericoExtranjero = "XEXX010101000";

    public static bool EsRfcGenerico(string rfc) =>
        string.Equals(rfc, RfcGenericoNacional, StringComparison.OrdinalIgnoreCase)
        || string.Equals(rfc, RfcGenericoExtranjero, StringComparison.OrdinalIgnoreCase);
}
