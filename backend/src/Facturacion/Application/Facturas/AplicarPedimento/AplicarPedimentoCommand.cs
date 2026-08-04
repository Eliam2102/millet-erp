using MediatR;

namespace Millet.Facturacion.Application.Facturas.AplicarPedimento;

/// <summary>
/// Aplica el pedimento a una factura retenida en <c>PendientePedimento</c> y
/// dispara su timbre (§3.bis.5, F7-PR2). El mismo pedimento se aplica a todas las
/// líneas que lo requieren (caso típico de una Hoja de Salida). Contra el stub de
/// timbrado hasta F12.
/// </summary>
public sealed record AplicarPedimentoCommand(
    Guid FacturaVentaId,
    string Pedimento,
    DateOnly? FechaDocAduanero,
    string? IdentificacionMercancia) : IRequest<AplicarPedimentoResponse>;

public sealed record AplicarPedimentoResponse(
    Guid Id,
    string Estado,
    string? Uuid,
    string Folio);
