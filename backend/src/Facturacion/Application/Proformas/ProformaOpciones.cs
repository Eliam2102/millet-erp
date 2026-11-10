namespace Millet.Facturacion.Application.Proformas;

/// <summary>
/// M-145 (86e3zr6d2): vigencia de la proforma en días naturales desde su emisión. Millet confirmó 15 días
/// naturales (respuesta del 9-nov al cuestionario de la junta del 13-nov). Sección <c>Facturacion:Proforma</c>.
/// </summary>
public sealed class ProformaOpciones
{
    public const string Seccion = "Facturacion:Proforma";
    public const int VigenciaDiasPorOmision = 15;

    /// <summary>Días naturales de vigencia (1 a 365).</summary>
    public int VigenciaDias { get; set; } = VigenciaDiasPorOmision;

    /// <summary>Último día de vigencia: la fecha de emisión (día fiscal en México) más los días naturales.</summary>
    public DateOnly VigenteHasta(DateTimeOffset generadaEnUtc) =>
        Timbrado.CfdiEmisionBuilder.DiaFiscal(generadaEnUtc).AddDays(Math.Clamp(VigenciaDias, 1, 365));
}
