namespace Millet.Contabilidad.Application.CuentaPuente;

/// <summary>
/// Sección <c>Contabilidad:CuentaPuente</c> (K10.5). <c>CodigoCuenta</c> = código de la cuenta puente recepción–factura en el catálogo,
/// contra cuyo saldo en balanza se concilia el reporte. Vacía por defecto: la define Contabilidad (en Development, DEMO-PUENTE).
/// </summary>
public sealed class CuentaPuenteOpciones
{
    public const string Seccion = "Contabilidad:CuentaPuente";
    public string CodigoCuenta { get; set; } = string.Empty;
}
