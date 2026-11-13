using MediatR;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Domain.Comprobantes;

namespace Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;

/// <summary>
/// Solicita la cancelación SAT 4.0 de un comprobante timbrado (§4.5, §7.1). F6.5:
/// queda pendiente de autorización del Contador General; no se envía al PAC hasta
/// que se autoriza. Valida la cadena (no cancelar un anticipo con NCs de
/// amortización vigentes, invariante 8).
/// </summary>
public sealed record SolicitarCancelacionCommand(
    Guid ComprobanteId,
    string MotivoSat,
    string? UuidSustituto) : IRequest<SolicitarCancelacionResponse>;

/// <summary>Estado de la solicitud y del comprobante tras solicitar, autorizar, rechazar o retirar.</summary>
public sealed record SolicitarCancelacionResponse(
    Guid SolicitudId,
    Guid ComprobanteId,
    string EstadoComprobante,
    string EstadoSolicitud,
    string? EstatusSat,
    string? MensajeError = null,
    int FirmasRequeridas = 1,
    int FirmasDadas = 0,
    string? PrimeraFirmaPor = null,
    string? Huella = null)
{
    public static SolicitarCancelacionResponse De(Comprobante comprobante, SolicitudCancelacion solicitud) =>
        new(solicitud.Id, comprobante.Id, comprobante.Estado.ToString(), solicitud.Estado.ToString(), solicitud.EstatusSat,
            MensajeSatParaUsuario(solicitud.MensajeError), solicitud.FirmasRequeridas, solicitud.FirmasDadas,
            solicitud.PrimeraFirmaPorNombre, solicitud.Huella);

    /// <summary>
    /// Prueba manual S3-4 (M18): el adaptador del PAC guarda «{mensaje del PAC} — {detalle del SAT}»;
    /// el mensaje del PAC llega en inglés y no le dice nada al usuario. Se muestra solo el detalle del SAT.
    /// </summary>
    internal static string? MensajeSatParaUsuario(string? mensaje)
    {
        if (string.IsNullOrWhiteSpace(mensaje)) return null;
        var corte = mensaje.LastIndexOf(" — ", StringComparison.Ordinal);
        var detalle = corte >= 0 ? mensaje[(corte + 3)..].Trim() : mensaje.Trim();
        return detalle.Length > 0 ? detalle : mensaje.Trim();
    }
}
