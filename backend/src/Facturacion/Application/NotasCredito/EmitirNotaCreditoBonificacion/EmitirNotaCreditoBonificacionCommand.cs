using MediatR;

namespace Millet.Facturacion.Application.NotasCredito.EmitirNotaCreditoBonificacion;

/// <summary>
/// Emite (sella + timbra) una NC por bonificación sobre una factura de venta
/// (§7.1 levantamiento; relación 01). El descuento comercial va por esta NC
/// posterior, <b>nunca</b> en el XML de la venta. Bloqueada si la factura origen
/// está cancelada (invariante 6). Contra el stub de timbrado hasta F12.
/// </summary>
public sealed record EmitirNotaCreditoBonificacionCommand(
    Guid FacturaVentaId,
    decimal MontoTotal,
    decimal? TasaIva,
    string? Descripcion,
    ReembolsoClienteRequest? Reembolso = null,
    string? Comentarios = null) : IRequest<EmitirNotaCreditoBonificacionResponse>;

/// <summary>
/// F6.3: «Devolver dinero al cliente». Al quedar timbrada la NC se pide a Tesorería el reembolso por su total, con esta
/// cuenta destino (CLABE validada). Exige el permiso <c>facturacion.notas-credito.reembolso-solicitar</c>.
/// </summary>
public sealed record ReembolsoClienteRequest(string Banco, string Clabe, string Titular);

public sealed record EmitirNotaCreditoBonificacionResponse(
    Guid Id,
    string Estado,
    string? Uuid,
    string Folio,
    decimal Total);
