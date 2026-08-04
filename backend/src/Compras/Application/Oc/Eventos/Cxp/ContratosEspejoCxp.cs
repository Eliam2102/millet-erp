namespace Millet.Compras.Application.Oc.Eventos.Cxp;

// ============================================================================
// Contratos ESPEJO de los IntegrationEvents publicados por CxP al topic
// `cuentas-por-pagar-events` que Compras consume (PR D — cierre outbound
// CxP → Compras).
//
// Mismo patrón que `CuentasPorPagar.Application.EventListeners.ContratosEspejo`:
// definimos copias locales para deserializar el payload de Service Bus sin
// acoplar bounded contexts cruzados. Cuando los contratos cambien, ambos
// lados migran juntos (versión en el EventType — `.v1`, `.v2`).
//
// Naming canónico (CLAUDE.md §Triada): {Agregado}{Verbo}Event con prefijo
// del agregado origen. Aquí usamos `Payload` para distinguir del
// `INotification` interno (`Domain.Ports.Cxp.FacturaProveedorRegistradaEvent`).
// ============================================================================

/// <summary>
/// Espejo de <c>cuentas_por_pagar.factura.registrada.v1</c>. Compras
/// usa <see cref="LineasAcumuladasOc"/> directamente como
/// <c>CantidadFacturadaAcumulada</c> en
/// <c>OrdenCompra.RegistrarFacturacionLinea</c>.
/// </summary>
public sealed record FacturaProveedorRegistradaPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaProveedorId,
    Guid OrdenCompraId,
    decimal TotalFactura,
    IReadOnlyList<LineaFacturadaPayload> Lineas,
    IReadOnlyList<LineaOcAcumuladaPayload> LineasAcumuladasOc);

public sealed record LineaFacturadaPayload(
    Guid LineaFacturaId,
    Guid? LineaOcId,
    decimal Cantidad,
    decimal Importe);

public sealed record LineaOcAcumuladaPayload(
    Guid LineaOcId,
    decimal CantidadAcumulada);

/// <summary>
/// Espejo de <c>cuentas_por_pagar.factura.pago-aplicado.v1</c> (cierra el
/// PLATFORM-TODO <c>&lt;TesoreriaEventListenerCompras&gt;</c>). CxP — dueño
/// de <c>importe_pagado</c> — publica el acumulado pagado por OC ya
/// calculado; Compras lo aplica con <c>OrdenCompra.RegistrarPago</c> (set,
/// no incremento) sin proyección espejo de facturas. La reversa de pago
/// publica el mismo EventType con el acumulado reducido — la OC reabre si
/// estaba Cerrada.
/// </summary>
public sealed record FacturaPagoAplicadoPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaProveedorId,
    Guid OrdenCompraId,
    decimal ImportePagadoFactura,
    decimal MontoPagadoAcumuladoOc);
