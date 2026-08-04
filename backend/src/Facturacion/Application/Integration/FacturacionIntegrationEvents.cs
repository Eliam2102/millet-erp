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
    DateTimeOffset? FechaTimbrado)
    : IntegrationEvent("facturacion.factura-venta.timbrada.v1", EmpresaId, OcurridoEn);

// ---- Factura de anticipo timbrada (asiento de anticipo MXP/USD) ----
public sealed record FacturaAnticipoTimbradaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaAnticipoId,
    Guid AnticipoId,
    string Uuid,
    decimal Total,
    string Moneda)
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
    Guid? AnticipoOrigenId)
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
    IReadOnlyList<ReppFacturaPagadaDetalle> FacturasPagadas)
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
    IReadOnlyList<CobroFormaPagoAplicada> FormasPago)
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
