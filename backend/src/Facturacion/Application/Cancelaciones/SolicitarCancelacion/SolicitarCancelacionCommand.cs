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
    string? EstatusSat)
{
    public static SolicitarCancelacionResponse De(Comprobante comprobante, SolicitudCancelacion solicitud) =>
        new(solicitud.Id, comprobante.Id, comprobante.Estado.ToString(), solicitud.Estado.ToString(), solicitud.EstatusSat);
}
