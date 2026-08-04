namespace Millet.CuentasPorCobrar.Application.EventListeners;

// ============================================================================
// Contratos ESPEJO de los eventos de Facturación que CxC consume (CXC-PR3).
// Mismo patrón que CxP/Almacén: copias locales para que el listener del
// Service Bus pueda deserializar sin acoplar bounded contexts cruzados.
// La compatibilidad se mantiene por la convención de versión en el
// EventType (vN); cambios incompatibles bumpean a v(N+1).
//
// Fuente: Facturacion.Application.Integration.FacturacionIntegrationEvents
// (extendidos aditivamente en este mismo PR con ReceptorRfc/Folio/MetodoPago/
// FechaTimbrado y el desglose de facturas pagadas del REPP).
// ============================================================================

public sealed record FacturaVentaTimbradaPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaVentaId,
    string Uuid,
    decimal Total,
    string Moneda,
    Guid? PedidoFacturableId,
    string ReceptorRfc,
    string ReceptorNombre,
    string Folio,
    string MetodoPago,
    DateTimeOffset? FechaTimbrado);

public sealed record ReppFacturaPagadaPayload(
    Guid FacturaVentaId,
    decimal ImportePagado,
    int NumParcialidad,
    string MonedaFactura,
    decimal SaldoInsoluto);

public sealed record ReciboPagoTimbradoPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid ReciboPagoId,
    string Uuid,
    decimal ImporteTotalPago,
    decimal GananciaPerdidaCambiaria,
    IReadOnlyList<ReppFacturaPagadaPayload>? FacturasPagadas);

public sealed record CobroMostradorRegistradoPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid CobroMostradorId,
    Guid CajaSesionId,
    Guid CajaId,
    Guid ComprobanteId,
    string TipoComprobante,
    string Origen,
    decimal Total);

public sealed record CobroMostradorCanceladoPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid CobroMostradorId,
    Guid ComprobanteId,
    decimal Total,
    bool ReversadoEnSesion);

public sealed record NotaCreditoTimbradaPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid NotaCreditoId,
    string Motivo,
    string Uuid,
    decimal Total,
    Guid? FacturaRelacionadaId,
    Guid? AnticipoOrigenId);

public sealed record ComprobanteCanceladoPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid ComprobanteId,
    string TipoComprobante,
    string Uuid);

public sealed record FacturaAnticipoTimbradaPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaAnticipoId,
    Guid AnticipoId,
    string Uuid,
    decimal Total,
    string Moneda);
