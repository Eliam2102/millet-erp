using MediatR;

namespace Millet.Facturacion.Application.Anticipos.VincularAnticipo;

/// <summary>
/// Vincula un anticipo a una factura de venta final (Momento 2, §6.3; relación
/// CFDI tipo 07). Registra el compromiso de amortización validando contra el
/// saldo disponible — <b>no reduce el saldo amortizado</b> (eso es la NC de
/// amortización M3, F4-PR2). La relación 07 en el XML del CFDI la materializa
/// el flujo de emisión de la factura final (F4-PR2 / F12).
/// </summary>
public sealed record VincularAnticipoCommand(
    Guid AnticipoId,
    Guid FacturaVentaId,
    decimal Importe) : IRequest<VincularAnticipoResponse>;

public sealed record VincularAnticipoResponse(
    Guid AnticipoId,
    Guid FacturaVentaId,
    decimal Importe,
    decimal Saldo,
    decimal SaldoDisponible,
    string Estado);
