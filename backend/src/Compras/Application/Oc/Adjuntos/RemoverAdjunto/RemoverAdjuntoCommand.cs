using MediatR;

namespace Millet.Compras.Application.Oc.Adjuntos.RemoverAdjunto;

/// <summary>
/// Remueve un adjunto de una OC en estado Borrador. Borra la fila +
/// el blob físico en el storage. Idempotente: si el adjunto no
/// existe, el handler devuelve sin error (mejor que 404 cuando el
/// cliente reintenta).
/// </summary>
public sealed record RemoverAdjuntoCommand(
    Guid OrdenCompraId,
    Guid AdjuntoId) : IRequest;
