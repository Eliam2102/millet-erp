namespace Millet.Facturacion.Domain.Cancelaciones;

/// <summary>
/// FSM de una <see cref="SolicitudCancelacion"/> ante el SAT (§4.5 diseño), con la
/// autorización previa del Contador General (F6.5):
/// <code>PendienteAutorizacion → {Solicitada → EnProceso → {Aceptada | Rechazada | Vencida} | RechazadaPorAutorizador | Retirada}</code>
/// <c>Vencida</c> = sin respuesta del receptor en 3 días hábiles → aceptación
/// tácita. El valor numérico (<c>short</c>) está fijo por ABI — agregar al final.
/// </summary>
public enum EstadoSolicitudCancelacion : short
{
    /// <summary>Registrada localmente; aún no enviada/confirmada por el PAC.</summary>
    Solicitada = 1,

    /// <summary>El PAC aceptó la solicitud; esperando la resolución del SAT/receptor (poller).</summary>
    EnProceso = 2,

    /// <summary>El SAT aceptó la cancelación: el comprobante queda Cancelado.</summary>
    Aceptada = 3,

    /// <summary>El SAT/receptor rechazó la cancelación: el comprobante sigue Timbrado.</summary>
    Rechazada = 4,

    /// <summary>Sin respuesta del receptor en plazo: aceptación tácita (equivale a Aceptada).</summary>
    Vencida = 5,

    /// <summary>Ingresos la solicitó; espera la autorización del Contador General. No se ha enviado al PAC.</summary>
    PendienteAutorizacion = 6,

    /// <summary>El Contador General no la autorizó (con motivo): el comprobante sigue vigente.</summary>
    RechazadaPorAutorizador = 7,

    /// <summary>Quien la solicitó la retiró antes de que se autorizara: el comprobante sigue vigente.</summary>
    Retirada = 8,
}
