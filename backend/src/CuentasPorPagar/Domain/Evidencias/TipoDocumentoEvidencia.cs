namespace Millet.CuentasPorPagar.Domain.Evidencias;

/// <summary>
/// Discriminador polimórfico del documento al que pertenece una
/// <see cref="EvidenciaAutorizacion"/> (§3.bis.4 del 01-diseno).
/// MVP solo wirea <see cref="FacturaProveedor"/> (F4-PR2); los otros
/// 3 valores quedan reservados para F6 (anticipo/notaCargo) y F7
/// (comprobación).
///
/// <para>
/// PLATFORM-TODO(&lt;EvidenciasParaTodosLosTiposDoc&gt;): cuando se
/// implementen los agregados <c>AnticipoProveedor</c>, <c>NotaCargo</c>
/// y <c>ComprobacionGastos</c>, extender el CHECK constraint de la
/// tabla <c>evidencias_autorizacion</c> y los handlers para validar
/// que el FK exista según el tipo.
/// </para>
/// </summary>
public enum TipoDocumentoEvidencia
{
    FacturaProveedor    = 1,
    AnticipoProveedor   = 2,
    NotaCargo           = 3,
    ComprobacionGastos  = 4,
}
