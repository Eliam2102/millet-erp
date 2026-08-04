using MediatR;
using Millet.Facturacion.Domain.Anticipos;

namespace Millet.Facturacion.Application.Anticipos.EmitirFacturaAnticipo;

/// <summary>
/// Emite (sella + timbra) una factura de anticipo CFDI (serie FANT) y abre su
/// saldo amortizable (<c>Anticipo</c>) — Momento 1 del ciclo de anticipos (§6.2
/// levantamiento). Siempre nominal; contra el stub de timbrado hasta F12.
///
/// <para>
/// <c>MetodoPago</c> determina el saldo inicial: <c>PUE</c> cobra el total al
/// emitir (saldo = total); <c>PPD</c> arranca el saldo en 0 y lo incrementan los
/// REPP (F6).
/// </para>
/// </summary>
public sealed record EmitirFacturaAnticipoCommand(
    Guid SucursalId,
    // Cliente / receptor (nominal — el anticipo no admite genérico)
    Guid ClienteId,
    string ReceptorRfc,
    string ReceptorNombre,
    string ReceptorRegimenFiscal,
    string ReceptorCodigoPostal,
    string ReceptorUsoCfdi,
    string ReceptorPais,
    // Emisor
    string RfcEmisor,
    string RegimenFiscalEmisor,
    // Pago
    string MetodoPago,
    string FormaPago,
    string Moneda,
    decimal? TipoCambio,
    // Anticipo
    TipoAnticipo TipoAnticipo,
    decimal MontoBase,
    decimal? TasaIvaTraslado,
    string? Descripcion,
    Guid? PedidoFacturableId,
    string? PedidoOrigenRef,
    long? ObraId,
    string? ObraNombre,
    // CAJAS-PR2 ([Decisión 12-D]): dimensión de la Capa A; null = sin canal
    // (anticipo administrativo) → bucket "Sin asignar".
    short? CanalVentaId = null) : IRequest<EmitirFacturaAnticipoResponse>;

public sealed record EmitirFacturaAnticipoResponse(
    Guid AnticipoId,
    Guid FacturaAnticipoId,
    string Estado,
    string? Uuid,
    string Folio,
    decimal Total,
    decimal Saldo);
