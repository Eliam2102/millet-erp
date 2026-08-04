namespace Millet.CuentasPorPagar.Domain.Viaticos;

/// <summary>
/// Ciclo completo de una <see cref="SolicitudViaticos"/> (§7.4.2 del
/// 00-levantamiento, F7-PR3). El flujo se diferencia entre solicitudes
/// que respetan política (Solicitada → AutorizadaPorJefe → Anticipada)
/// y las que exceden política (Solicitada → AutorizadaPorJefe →
/// RequiereDireccionFinanzas → AutorizadaCompleta → Anticipada).
/// </summary>
public enum EstadoSolicitudViaticos
{
    /// <summary>Capturada por el empleado, pendiente de firma del jefe directo.</summary>
    Solicitada                  = 1,

    /// <summary>Firmada por el jefe directo y dentro de política — lista para Tesorería.</summary>
    AutorizadaPorJefe           = 2,

    /// <summary>Firmada por jefe pero excede política — requiere DF como Nivel 2.</summary>
    RequiereDireccionFinanzas   = 3,

    /// <summary>Firmada por jefe + DF (excedía política) — lista para Tesorería.</summary>
    AutorizadaCompleta          = 4,

    /// <summary>Tesorería pagó el préstamo al empleado; en viaje.</summary>
    Anticipada                  = 5,

    /// <summary>Empleado capturó comprobación al regresar; pendiente revisión CxP.</summary>
    ComprobacionCapturada       = 6,

    /// <summary>CxP liberó; diferencia (a favor o contra empleado) calculada; ciclo cerrado.</summary>
    Liquidada                   = 7,

    /// <summary>Rechazada por jefe o por DF antes del anticipo.</summary>
    Rechazada                   = 8,
}
