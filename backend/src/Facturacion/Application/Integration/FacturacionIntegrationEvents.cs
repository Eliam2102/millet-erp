using Millet.SharedKernel.Application.Integration;

namespace Millet.Facturacion.Application.Integration;

/// <summary>
/// Eventos de integración publicados por Facturación (§8.1 diseño) al topic
/// <c>facturacion-events</c> vía Outbox. Naming canónico
/// <c>facturacion.{recurso}.{acción}.vN</c>. Los consumen Contabilidad, CxC y los
/// write-backs a orígenes (A+W / Origenes).
/// </summary>
// ---- Factura de venta timbrada (ingreso reconocido; saldo por cobrar; 115) ----
// CXC-PR3: extensión ADITIVA con los datos que la proyección factura_cartera
// de CxC necesita (ReceptorRfc para correlacionar cliente vía IClienteReadPort
// — decisión con el owner 2026-07-13: RFC + read port, NO ClienteId persistido
// en Comprobante —, Folio para display, MetodoPago PUE/PPD y FechaTimbrado
// para calcular vencimiento). Aditivo = consumidores existentes no se afectan;
// cambios incompatibles bumpean a v2.
public sealed record FacturaVentaTimbradaIntegrationEvent(
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
    DateTimeOffset? FechaTimbrado,
    // U1.6: bloque contable opcional (al final; JSON antiguo sin estos campos
    // deserializa a null). Mismos nombres que G1.6 en CxP/Almacén/Tesorería.
    decimal? Subtotal = null,
    decimal? Descuento = null,
    decimal? Iva = null,
    decimal? RetencionesTotal = null,
    IReadOnlyList<RetencionContablePayload>? Retenciones = null,
    Guid? SucursalId = null,
    Guid? ClienteId = null,
    decimal? TipoCambio = null,
    DateOnly? FechaContable = null,
    IReadOnlyList<FacturaLineaContablePayload>? Lineas = null)
    : IntegrationEvent("facturacion.factura-venta.timbrada.v1", EmpresaId, OcurridoEn);

/// <summary>
/// Retención del CFDI (U1.6, misma forma que <c>RetencionDetallePayload</c>
/// de G1.6): <c>Impuesto</c> = código SAT (001 ISR, 002 IVA); <c>Tasa</c> null
/// a nivel comprobante.
/// </summary>
public sealed record RetencionContablePayload(string Impuesto, decimal? Tasa, decimal Importe);

/// <summary>
/// Línea de factura para separar ventas por tipo de producto (U1.6).
/// <c>Importe</c> es el del CFDI (antes de descuento); base = Importe − Descuento.
/// <c>TipoProducto</c> es el tipo A+W del producto (ADM-07) mientras se cierra
/// el catálogo contable de tipos con Contabilidad.
/// </summary>
public sealed record FacturaLineaContablePayload(
    Guid? ProductoId,
    string ClaveProdServSat,
    string? TipoProducto,
    decimal Importe,
    decimal Descuento,
    decimal Iva);

// ---- Factura de anticipo timbrada (asiento de anticipo MXP/USD) ----
public sealed record FacturaAnticipoTimbradaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaAnticipoId,
    Guid AnticipoId,
    string Uuid,
    decimal Total,
    string Moneda,
    // U1.6: bloque contable opcional (al final).
    decimal? Subtotal = null,
    decimal? Iva = null,
    Guid? SucursalId = null,
    Guid? ClienteId = null,
    decimal? TipoCambio = null,
    DateOnly? FechaContable = null)
    : IntegrationEvent("facturacion.factura-anticipo.timbrada.v1", EmpresaId, OcurridoEn);

// ---- Nota de crédito timbrada (amortización / bonificación) ----
public sealed record NotaCreditoTimbradaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid NotaCreditoId,
    string Motivo,
    string Uuid,
    decimal Total,
    Guid? FacturaRelacionadaId,
    Guid? AnticipoOrigenId,
    // U1.6: bloque contable opcional (al final). La NC de Ranura se
    // contabiliza como descuento sobre ventas (Plano C1 #9) con este desglose.
    decimal? Subtotal = null,
    decimal? Iva = null,
    Guid? SucursalId = null,
    Guid? ClienteId = null,
    string? Moneda = null,
    decimal? TipoCambio = null,
    DateOnly? FechaContable = null)
    : IntegrationEvent("facturacion.nota-credito.timbrada.v1", EmpresaId, OcurridoEn);

/// <summary>
/// Desglose por factura pagada dentro de un REPP (CXC-PR3). El sufijo
/// "Detalle" evita colisión con el DTO homónimo de EmitirReppCommand.
/// </summary>
public sealed record ReppFacturaPagadaDetalle(
    Guid FacturaVentaId,
    decimal ImportePagado,
    int NumParcialidad,
    string MonedaFactura,
    decimal SaldoInsoluto);

// ---- REPP timbrado (cobro confirmado; ganancia/pérdida cambiaria; 70) ----
// CXC-PR3: extensión ADITIVA con el desglose por factura (el publisher ya
// tiene repp.FacturasPagadas en scope) — sin él, CxC no puede aplicar los
// pagos a la proyección factura_cartera sin leer tablas de facturacion
// (regla de oro de la triada).
public sealed record ReciboPagoTimbradoIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid ReciboPagoId,
    string Uuid,
    decimal ImporteTotalPago,
    decimal GananciaPerdidaCambiaria,
    IReadOnlyList<ReppFacturaPagadaDetalle> FacturasPagadas,
    Guid? MovimientoBancarioId = null)
    : IntegrationEvent("facturacion.recibo-pago.timbrado.v1", EmpresaId, OcurridoEn);

// ---- Comprobante cancelado (reversa fiscal/financiera) ----
public sealed record ComprobanteCanceladoIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid ComprobanteId,
    string TipoComprobante,
    string Uuid)
    : IntegrationEvent("facturacion.comprobante.cancelado.v1", EmpresaId, OcurridoEn);

// ---- Sesión de caja abierta (CAJAS-PR3, 12-cajas.md §10) ----
public sealed record CajaSesionAbiertaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid CajaSesionId,
    Guid CajaId,
    Guid SucursalId,
    Guid ResponsableUsuarioId,
    DateOnly DiaOperacion,
    decimal FondoApertura,
    Guid? AutorizacionAperturaId)
    : IntegrationEvent("facturacion.caja-sesion.abierta.v1", EmpresaId, OcurridoEn);

/// <summary>Total por forma de pago del corte de una sesión cerrada.</summary>
public sealed record CajaSesionCorteTotal(string FormaPago, decimal MontoSistema, decimal? MontoDeclarado);

// ---- Sesión de caja cerrada: totales por forma + diferencia de efectivo ----
// Consumidores: Tesorería (expectativa de depósito Caja→Banco, TES-PR7 —
// cerró PLATFORM-TODO(<TesoreriaCajaSesion>); espejo subset en
// backend/src/Tesoreria/Application/EventListeners/ContratosEspejoFacturacion.cs)
// y Contabilidad futura (póliza de sobrante/faltante; 12-cajas.md §11).
public sealed record CajaSesionCerradaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid CajaSesionId,
    Guid CajaId,
    Guid SucursalId,
    Guid ResponsableUsuarioId,
    DateOnly DiaOperacion,
    decimal FondoApertura,
    decimal EfectivoTeorico,
    decimal EfectivoDeclarado,
    decimal Diferencia,
    bool CierreExtemporaneo,
    IReadOnlyList<CajaSesionCorteTotal> Cortes)
    : IntegrationEvent("facturacion.caja-sesion.cerrada.v1", EmpresaId, OcurridoEn);

/// <summary>Forma de pago aplicada en un cobro de mostrador.</summary>
public sealed record CobroFormaPagoAplicada(string FormaPago, decimal Importe);

// ---- Cobro de mostrador registrado (CAJAS-PR4, 12-cajas.md §10) ----
public sealed record CobroMostradorRegistradoIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid CobroMostradorId,
    Guid CajaSesionId,
    Guid CajaId,
    Guid ComprobanteId,
    string TipoComprobante,
    string Origen,
    decimal Total,
    IReadOnlyList<CobroFormaPagoAplicada> FormasPago,
    // U1.6: bloque contable opcional (al final). IvaCobrado solo cuando el
    // comprobante cobrado es una factura (proporcional al cobro); null en REPP.
    Guid? SucursalId = null,
    Guid? ClienteId = null,
    string? Moneda = null,
    decimal? TipoCambio = null,
    decimal? IvaCobrado = null)
    : IntegrationEvent("facturacion.cobro-mostrador.registrado.v1", EmpresaId, OcurridoEn);

// ---- Cobro de mostrador cancelado (reversa en sesión abierta o ajuste pendiente [12-C]) ----
public sealed record CobroMostradorCanceladoIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid CobroMostradorId,
    Guid ComprobanteId,
    decimal Total,
    bool ReversadoEnSesion)
    : IntegrationEvent("facturacion.cobro-mostrador.cancelado.v1", EmpresaId, OcurridoEn);
