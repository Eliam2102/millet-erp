using MediatR;

namespace Millet.Facturacion.Application.Repp.EmitirRepp;

/// <summary>
/// Emite (sella + timbra) un REPP (CFDI tipo P, complemento Pago 2.0) que cubre
/// una o varias facturas PPD del mismo cliente, con parcialidades y diferencia
/// cambiaria automática (§4.7, §11, D14). Contra el stub de timbrado hasta F12.
/// </summary>
public sealed record EmitirReppCommand(
    Guid SucursalId,
    DateTimeOffset FechaPago,
    string MonedaPago,
    decimal? TcPago,
    string FormaPagoReal,
    string? CuentaOrdenante,
    string? CuentaBeneficiaria,
    string? ReferenciaPago,
    IReadOnlyList<ReppFacturaPago> Facturas,
    // CAJAS-PR2 ([Decisión 12-D]): dimensión de la Capa A; null = sin canal
    // (cobro bancario/administrativo) → bucket "Sin asignar".
    short? CanalVentaId = null,
    // PR gemelo TES-PR7: empresa para invocaciones fuera de un request HTTP
    // (TesoreriaEventListenerWorker). El claim del JWT SIEMPRE tiene
    // precedencia — un caller HTTP no puede cruzar de empresa por body.
    Guid? EmpresaId = null) : IRequest<EmitirReppResponse>;

/// <summary>Factura cubierta por el pago, con el importe aplicado (en la moneda de la factura).</summary>
public sealed record ReppFacturaPago(Guid FacturaVentaId, decimal ImportePagado);

public sealed record EmitirReppResponse(
    Guid Id,
    string Estado,
    string? Uuid,
    string Folio,
    decimal ImporteTotalPago,
    decimal GananciaPerdidaCambiariaTotal,
    IReadOnlyList<ReppFacturaPagada> Facturas);

public sealed record ReppFacturaPagada(
    Guid FacturaVentaId,
    int NumParcialidad,
    decimal ImportePagado,
    decimal SaldoInsoluto,
    decimal GananciaPerdidaCambiaria);
