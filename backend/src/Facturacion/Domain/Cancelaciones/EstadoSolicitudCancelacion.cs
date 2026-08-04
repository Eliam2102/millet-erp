namespace Millet.Facturacion.Domain.Cancelaciones;

/// <summary>
/// FSM de una <see cref="SolicitudCancelacion"/> ante el SAT (§4.5 diseño):
/// <code>Solicitada → EnProceso → {Aceptada | Rechazada | Vencida}</code>
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
}
