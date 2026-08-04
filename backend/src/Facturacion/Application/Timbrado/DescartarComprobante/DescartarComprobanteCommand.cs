using MediatR;

namespace Millet.Facturacion.Application.Timbrado.DescartarComprobante;

/// <summary>
/// Descarta un comprobante en <c>TimbradoFallido</c> que NO se reintentará
/// ([Decisión 01-G] G3) — pedido cancelado en origen, captura errónea de
/// raíz. Terminal: quema el folio interno a conciencia (única fuente de
/// huecos en consecutivos junto al fallo de persistencia post-reserva) y
/// libera el pedido facturable si esta factura lo tenía tomado (G5). El
/// comprobante nunca llegó al SAT, así que no hay cancelación fiscal.
/// </summary>
public sealed record DescartarComprobanteCommand(Guid ComprobanteId)
    : IRequest<DescartarComprobanteResponse>;

public sealed record DescartarComprobanteResponse(
    Guid Id,
    string Tipo,
    string Estado,
    string Folio,
    Guid? PedidoLiberadoId,
    int Version);
