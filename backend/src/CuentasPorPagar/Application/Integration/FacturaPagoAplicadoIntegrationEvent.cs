using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>
/// EventType <c>cuentas_por_pagar.factura.pago-aplicado.v1</c>. Publicado
/// al outbox cuando el <c>ImportePagado</c> de una factura ligada a OC
/// cambia (pago aplicado o revertido desde Tesorería). Consumidor:
/// Compras — aplica <see cref="MontoPagadoAcumuladoOc"/> directo en
/// <c>OrdenCompra.RegistrarPago</c> para derivar el sub-estado Pago y
/// disparar el cierre automático de la OC (las 3 dimensiones completas
/// → <c>OrdenCompraCerradaEvent</c>).
///
/// <para>
/// <see cref="MontoPagadoAcumuladoOc"/> es el acumulado TOTAL pagado de
/// TODAS las facturas de la OC después de este evento (patrón
/// "acumulados calculados por el publisher": CxP es dueño de
/// <c>importe_pagado</c>; Compras solo lo proyecta, sin espejo local).
/// Repetir el mismo evento es no-op (set, no incremento). La reversa de
/// pago publica el mismo EventType con el acumulado reducido — Compras
/// reabre la OC si estaba Cerrada (F5-PR1 ya lo soporta).
/// </para>
///
/// <para>
/// Espejo del consumidor:
/// <c>backend/src/Compras/Application/Oc/Eventos/Cxp/ContratosEspejoCxp.cs</c>.
/// El filtro SB de <c>compras-subscription</c> debe incluir este
/// EventType (infra/modules/servicebus.bicep).
/// </para>
/// </summary>
public sealed record FacturaPagoAplicadoIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaProveedorId,
    Guid OrdenCompraId,
    decimal ImportePagadoFactura,
    decimal MontoPagadoAcumuladoOc)
    : IntegrationEvent("cuentas_por_pagar.factura.pago-aplicado.v1", EmpresaId, OcurridoEn);
