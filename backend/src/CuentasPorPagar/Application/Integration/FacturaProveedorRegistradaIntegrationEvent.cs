using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>
/// EventType <c>cuentas_por_pagar.factura.registrada.v1</c>. Compras
/// suscribe (actualiza <c>CantidadFacturada</c> y
/// <c>SubEstadoFacturacion</c> de OC, §8.1 del 01-diseno). Almacén
/// suscribe (variante B: concilia recepción pendiente).
///
/// <para>
/// <b>LineasAcumuladasOc</b> (PR D — cierre outbound CxP → Compras): CxP
/// es el dueño del dato y publica el acumulado total facturado para cada
/// <c>LineaOcId</c> tras aplicar esta factura. El listener de Compras
/// invoca <c>OrdenCompra.RegistrarFacturacionLinea</c> con ese acumulado
/// directamente — sin proyecciones espejo en Compras. Campo backward-
/// compatible (default lista vacía); consumidores que no lo usen
/// (Almacén variante B) lo ignoran.
/// </para>
/// </summary>
public sealed record FacturaProveedorRegistradaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaProveedorId,
    Guid OrdenCompraId,
    decimal TotalFactura,
    IReadOnlyList<LineaFacturadaPayload> Lineas,
    IReadOnlyList<LineaOcAcumuladaPayload> LineasAcumuladasOc,
    // G1.6: bloque contable opcional (al final; JSON antiguo sin estos campos deserializa a null).
    Guid? ProveedorId = null,
    string? Uuid = null,
    decimal? Subtotal = null,
    decimal? Iva = null,
    decimal? Retenciones = null,
    IReadOnlyList<RetencionDetallePayload>? RetencionesDetalle = null,
    string? Moneda = null,
    decimal? TipoCambio = null,
    Guid? SucursalId = null,
    Guid? CentroCostoId = null)
    : IntegrationEvent("cuentas_por_pagar.factura.registrada.v1", EmpresaId, OcurridoEn);

public sealed record LineaFacturadaPayload(
    Guid LineaFacturaId,
    Guid? LineaOcId,
    decimal Cantidad,
    decimal Importe,
    Guid? CentroCostoId = null);

/// <summary>
/// Retención del CFDI: <c>Impuesto</c> = código SAT (001 ISR, 002 IVA,
/// 003 IEPS); <c>Tasa</c> null a nivel comprobante.
/// </summary>
public sealed record RetencionDetallePayload(string Impuesto, decimal? Tasa, decimal Importe);

/// <summary>
/// Acumulado total facturado para una <c>LineaOcId</c> tras aplicar la
/// factura corriente (incluye delta de esta factura + facturas previas
/// vigentes contra la misma línea OC). Compras lo usa como
/// <c>CantidadFacturadaAcumulada</c> directo en
/// <c>OrdenCompra.RegistrarFacturacionLinea</c>.
/// </summary>
public sealed record LineaOcAcumuladaPayload(
    Guid LineaOcId,
    decimal CantidadAcumulada);
