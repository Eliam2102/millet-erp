using MediatR;
using Millet.Facturacion.Domain.Facturas;

namespace Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;

/// <summary>
/// Emite (sella + timbra en una operación, D11) una factura de venta CFDI vía
/// <c>ICfdiTimbradoPort</c>. El RFC + régimen del emisor viajan en el comando
/// (prellenados por el FE desde emisor-defaults); la razón social y el
/// LugarExpedicion se resuelven del master de la empresa al emitir (F12-PR1).
/// <c>ReceptorEsGenerico</c> lo deriva el handler del RFC (<c>XAXX/XEXX</c>).
/// </summary>
public sealed record EmitirFacturaVentaCommand(
    Guid SucursalId,
    // Receptor (snapshot al emitir)
    string ReceptorRfc,
    string ReceptorNombre,
    string ReceptorRegimenFiscal,
    string ReceptorCodigoPostal,
    string ReceptorUsoCfdi,
    string ReceptorPais,
    // Emisor
    string RfcEmisor,
    string RegimenFiscalEmisor,
    // Datos de pago
    string MetodoPago,
    string FormaPago,
    string Moneda,
    decimal? TipoCambio,
    // Ejes ortogonales (§7, D3)
    short CanalVenta,
    ComportamientoFiscal ComportamientoFiscal,
    long? ObraId,
    string? ObraNombre,
    bool FacturaAgrupada,
    IReadOnlyList<EmitirFacturaVentaLinea> Lineas,
    // Anticipos a amortizar (M2+M3, F4-PR2). Si viene, tras timbrar la factura se
    // autogenera + timbra una NC de amortización por cada anticipo y se reduce su
    // saldo, todo en la misma transacción.
    IReadOnlyList<AnticipoAAmortizar>? Anticipos = null,
    // Complemento de Comercio Exterior (F7-PR1). Solo válido si el comportamiento
    // fiscal es ExportacionConCce; se adjunta antes de timbrar.
    EmitirFacturaVentaCce? Cce = null,
    // Autorización del Contador General (F9). Obligatoria si el comportamiento es
    // VentaActivoFijo; sin ella no se timbra.
    Guid? AutorizacionId = null,
    // Pedido facturable de origen (B2, FE-F1). Si viene, el pedido debe estar
    // Importado; al emitir queda Facturado (re-facturable sólo tras cancelar).
    Guid? PedidoFacturableId = null) : IRequest<EmitirFacturaVentaResponse>;

/// <summary>Anticipo a amortizar contra la factura final, con el importe a aplicar.</summary>
public sealed record AnticipoAAmortizar(Guid AnticipoId, decimal Importe);

/// <summary>Datos del Complemento de Comercio Exterior para una factura de exportación (F7-PR1).</summary>
public sealed record EmitirFacturaVentaCce(
    string TipoOperacion,
    string Incoterm,
    decimal TcDof,
    string ReceptorNumRegIdTrib,
    string ReceptorPaisResidencia,
    IReadOnlyList<EmitirFacturaVentaCceLinea> Lineas,
    // F12-PR3: clave de pedimento, certificado de origen y domicilio del
    // receptor extranjero (CCE 2.0). Nullable/default por compatibilidad con el
    // FE actual; el builder valida Estado/CP al timbrar real.
    string? ClaveDePedimento = null,
    bool CertificadoOrigen = false,
    string? ReceptorDomicilioCalle = null,
    string? ReceptorDomicilioEstado = null,
    string? ReceptorDomicilioCodigoPostal = null);

/// <summary>Mercancía del CCE (datos aduaneros por línea).</summary>
public sealed record EmitirFacturaVentaCceLinea(
    string FraccionArancelaria,
    string UnidadAduana,
    decimal CantidadAduana,
    decimal ValorUnitarioAduana,
    decimal ValorDolares,
    bool AplicaIva0);

/// <summary>Línea de la factura a emitir. Los importes de impuestos los calcula el dominio desde las tasas.</summary>
public sealed record EmitirFacturaVentaLinea(
    Guid? ProductoId,
    string ClaveProdServSat,
    string Descripcion,
    string ClaveUnidadSat,
    decimal Cantidad,
    decimal ValorUnitario,
    decimal Descuento,
    string ObjetoImp,
    decimal? TasaIvaTraslado,
    decimal? TasaRetencionIva,
    decimal? TasaRetencionIsr,
    // §3.bis.5: producto de importación en su primera venta → la factura se retiene
    // en PendientePedimento hasta aplicar el pedimento (F7-PR2).
    bool RequierePedimento = false);

public sealed record EmitirFacturaVentaResponse(
    Guid Id,
    string Estado,
    string? Uuid,
    string Folio,
    decimal Total,
    int Version,
    IReadOnlyList<NotaCreditoAmortizacionEmitida>? NotasCreditoAmortizacion = null,
    // RANURA-PR2: NC automática de la ranura del pedido A+W (relación 01).
    Millet.Facturacion.Application.NotasCredito.NotaCreditoRanuraEmitida? NotaCreditoRanura = null);

/// <summary>NC de amortización autogenerada al emitir la factura con anticipos (F4-PR2).</summary>
public sealed record NotaCreditoAmortizacionEmitida(
    Guid Id,
    Guid AnticipoId,
    string Folio,
    string? Uuid,
    decimal Total,
    decimal SaldoAnticipoRestante);
