using MediatR;

namespace Millet.Facturacion.Application.Timbrado.ReintentarTimbrado;

/// <summary>
/// Reintenta el timbrado de un comprobante en <c>TimbradoFallido</c> —
/// cualquier tipo (factura de venta, factura de anticipo, nota de crédito,
/// REPP, carta porte). Un rechazo del PAC no timbró nada ante el SAT, así
/// que el MISMO comprobante (mismo folio interno) se reabre a Borrador y se
/// re-timbra; la <c>CfdiEmision</c> se reconstruye desde los datos ACTUALES
/// del agregado y sus catálogos (una corrección de datos entre intentos
/// viaja en el reintento) con fecha CFDI nueva (regla SAT de 72h).
///
/// <para>
/// <see cref="ConfirmarNoDuplicado"/>: los fallidos por códigos AMBIGUOS
/// (<c>PAC_TIMEOUT</c>, <c>PAC_SIN_RESPUESTA</c>, <c>PAC_RESPUESTA_INCOMPLETA</c>)
/// pudieron haberse timbrado del lado del PAC aunque nunca llegó la
/// respuesta — reintentarlos a ciegas puede DUPLICAR el CFDI ante el SAT.
/// Para esos casos el caller debe verificar primero en el dashboard de
/// FiscalAPI (runbook 08 §5.bis) y mandar <c>true</c>. Los rechazos limpios
/// (400 de validación, CFDI40xxx, CSD_NO_CONFIGURADO) no lo requieren.
/// </para>
/// </summary>
public sealed record ReintentarTimbradoCommand(
    Guid ComprobanteId,
    bool ConfirmarNoDuplicado = false) : IRequest<ReintentarTimbradoResponse>;

public sealed record ReintentarTimbradoResponse(
    Guid Id,
    string Tipo,
    string Estado,
    string? Uuid,
    string Folio,
    string? TimbradoErrorCodigo,
    string? TimbradoErrorMensaje,
    int Version);
