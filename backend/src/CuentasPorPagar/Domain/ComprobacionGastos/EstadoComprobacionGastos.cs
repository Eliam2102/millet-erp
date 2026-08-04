namespace Millet.CuentasPorPagar.Domain.ComprobacionGastos;

/// <summary>
/// Ciclo de vida de una <see cref="ComprobacionGastos"/> según §7.4 del
/// 00-levantamiento.
/// </summary>
public enum EstadoComprobacionGastos
{
    /// <summary>Capturando líneas. Editable.</summary>
    Borrador         = 1,

    /// <summary>Enviada para revisión del responsable de sucursal/área.</summary>
    PorRevisar       = 2,

    /// <summary>Autorizada por el responsable; lista para aplicación contable.</summary>
    Autorizada       = 3,

    /// <summary>Aplicada al ledger; pasa a Tesorería para reposición de caja.</summary>
    Aplicada         = 4,

    /// <summary>Rechazada por el responsable; queda cerrada con motivo.</summary>
    Rechazada        = 5,

    /// <summary>
    /// Estado intermedio para Aduanales (F7-PR2): firmada Nivel 1
    /// (Comercio Exterior) pendiente de Nivel 2 (Dirección de Finanzas).
    /// Solo aplicable a <see cref="TipoComprobacionGastos.GastosAduanales"/>.
    /// </summary>
    AutorizadaNivel1 = 6,
}
