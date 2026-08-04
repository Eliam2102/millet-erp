namespace Millet.Facturacion.Domain.Comprobantes;

/// <summary>
/// FSM del ciclo de timbrado de un <see cref="Comprobante"/> (§4.4 diseño).
/// Modelada asíncrono-tolerante (Decisión 01-C): <see cref="TimbradoEnProceso"/>
/// y <see cref="CancelacionPendiente"/> permiten que el flujo no asuma
/// respuesta síncrona del PAC; un worker resuelve los pendientes.
///
/// <code>
/// Borrador ──► PendientePedimento ──► Borrador
/// Borrador ──► TimbradoEnProceso ──► Timbrado | TimbradoFallido
/// TimbradoFallido ──► Borrador (reintento, mismo folio) | Descartada (terminal)
/// Timbrado ──► CancelacionPendiente ──► Cancelado | Timbrado (rechazada)
/// </code>
///
/// <para>El valor numérico (<c>short</c>) está fijo por ABI — agregar nuevos al
/// final, nunca renumerar.</para>
/// </summary>
public enum EstadoTimbrado : short
{
    /// <summary>Borrador local, sin timbrar. Editable.</summary>
    Borrador = 1,

    /// <summary>Retenido por requerir pedimento aún no disponible (§3.bis.5). Solo facturas con líneas que lo marcan.</summary>
    PendientePedimento = 2,

    /// <summary>Enviado al PAC; el timbre puede resolverse asíncronamente.</summary>
    TimbradoEnProceso = 3,

    /// <summary>Timbrado por el SAT: UUID + sellos disponibles. Inmutable.</summary>
    Timbrado = 4,

    /// <summary>Error definitivo del PAC; corregible → vuelve a Borrador.</summary>
    TimbradoFallido = 5,

    /// <summary>Solicitud de cancelación SAT 4.0 en curso (esperando aceptación del receptor).</summary>
    CancelacionPendiente = 6,

    /// <summary>Cancelado ante el SAT. El CFDI nunca se borra (inmutable).</summary>
    Cancelado = 7,

    /// <summary>
    /// Fallida descartada a conciencia: no se reintentará (pedido cancelado en
    /// origen, captura errónea de raíz). Terminal; quema el folio interno y
    /// libera el pedido facturable ([Decisión 01-G] G3). Nunca llegó al SAT.
    /// </summary>
    Descartada = 8,
}
