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
    IReadOnlyList<LineaOcAcumuladaPayload> LineasAcumuladasOc)
    : IntegrationEvent("cuentas_por_pagar.factura.registrada.v1", EmpresaId, OcurridoEn);

public sealed record LineaFacturadaPayload(
    Guid LineaFacturaId,
    Guid? LineaOcId,
    decimal Cantidad,
    decimal Importe);

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
