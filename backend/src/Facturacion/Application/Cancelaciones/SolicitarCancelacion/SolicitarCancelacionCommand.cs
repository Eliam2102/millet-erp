using MediatR;

namespace Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;

/// <summary>
/// Solicita la cancelación SAT 4.0 de un comprobante timbrado (§4.5, §7.1). Valida
/// la cadena (no cancelar un anticipo con NCs de amortización vigentes, invariante
/// 8) y, si el PAC acepta, cancela el CFDI y revierte sus efectos (la factura
/// final libera el pedido → re-facturable; el anticipo pasa a Cancelado). Contra
/// el stub de cancelación hasta F12.
/// </summary>
public sealed record SolicitarCancelacionCommand(
    Guid ComprobanteId,
    string MotivoSat,
    string? UuidSustituto) : IRequest<SolicitarCancelacionResponse>;

public sealed record SolicitarCancelacionResponse(
    Guid SolicitudId,
    Guid ComprobanteId,
    string EstadoComprobante,
    string EstadoSolicitud,
    string? EstatusSat);
