namespace Millet.Facturacion.Application.Cajas.Sesiones;

/// <summary>
/// Configuración de la Capa B de Cajas (CAJAS-PR3). Defaults en código —
/// ningún ambiente requiere setting explícito; se puede sobrescribir vía
/// <c>Facturacion:Cajas:*</c>.
/// </summary>
public sealed class CajasOptions
{
    public const string SectionName = "Facturacion:Cajas";

    /// <summary>Vigencia de la autorización consumible de apertura de caja ajena (default 30 min, `[Decisión 12-1]`).</summary>
    public TimeSpan VigenciaAutorizacionApertura { get; set; } = TimeSpan.FromMinutes(30);
}
