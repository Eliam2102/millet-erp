using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.Integration;

/// <summary>
/// EventType <c>almacen.oc_devolucion.registrada.v1</c>. Publicado al
/// outbox cuando una <c>DevolucionAProveedor</c> pasa a estado
/// Registrada (sub-flujo 8.B, F6-PR1). Consumidores:
/// <list type="bullet">
///   <item><b>CxP</b>: genera <c>NotaCargo</c> contra el proveedor por
///   el monto de la devolución, y eventualmente emite NC fiscal tipo
///   CFDI 03 que cierra el ciclo (<c>NotaCreditoProveedorRegistradaEvent</c>
///   con <c>TipoRelacionCfdi=3</c>).</item>
///   <item><b>Compras</b>: decrementa <c>CantidadRecibida</c> en la
///   línea de OC asociada.</item>
/// </list>
/// </summary>
public sealed record OcDevolucionRegistradaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid DevolucionId,
    string FolioMovimiento,
    Guid ProveedorId,
    Guid? RecepcionOrigenId,
    Guid? FacturaProveedorOrigenId,
    Guid? OrdenCompraOrigenId,
    string Motivo,
    decimal MontoTotalMxn,
    IReadOnlyList<LineaDevolucionProveedorPayload> Lineas,
    // G1.6: dimensiones contables opcionales, aditivas (sin bump de versión).
    Guid? AlmacenId = null,
    Guid? SucursalId = null)
    : IntegrationEvent("almacen.oc_devolucion.registrada.v1", EmpresaId, OcurridoEn);

/// <summary>
/// <c>LineaOcId</c> se toma de la línea de recepción origen verificada,
/// preservando la correspondencia aun si la OC repite un artículo.
/// Compras la usa para decrementar <c>CantidadRecibida</c>.
/// Puede ser NULL en recepciones históricas sin referencia por línea.
/// </summary>
public sealed record LineaDevolucionProveedorPayload(
    Guid LineaDevolucionId,
    Guid? LineaRecepcionOrigenId,
    Guid? LineaOcId,
    Guid ArticuloId,
    string UnidadMedida,
    decimal Cantidad,
    decimal CostoUnitarioMxn,
    decimal MontoTotalMxn,
    // Aditivo (86e3pc50v): cantidad devuelta en unidad de la línea de OC (Cantidad va en unidad base del inventario).
    decimal? CantidadDocumento = null);

/// <summary>
/// EventType <c>almacen.oc_devolucion.conciliada.v1</c>. Publicado al outbox cuando Almacén
/// concilia una <c>DevolucionAProveedor</c> con la NC fiscal tipo 03 (consume
/// <c>cuentas_por_pagar.nc-fiscal-devolucion.recibida.v1</c>, con el Id exacto de la devolución).
/// Consumidor: <b>Compras</b>, que fija <c>CantidadAcreditada</c> por línea de OC: lo acreditado ya no se espera
/// y la OC puede cerrar.
/// <para>
/// <c>LineasAcreditadasOc</c> lleva el acumulado total post-evento (todas las devoluciones
/// ConciliadaConNcFiscal de la OC), en la unidad de la línea de OC, para toda línea tocada por alguna
/// devolución conciliada; es idempotente por diseño (Compras asigna, no suma).
/// </para>
/// </summary>
public sealed record OcDevolucionConciliadaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid OrdenCompraId,
    Guid DevolucionId,
    IReadOnlyList<LineaOcAcreditadaPayload> LineasAcreditadasOc)
    : IntegrationEvent("almacen.oc_devolucion.conciliada.v1", EmpresaId, OcurridoEn);

/// <summary><c>CantidadAcreditadaAcumulada</c> en la unidad de la línea de OC.</summary>
public sealed record LineaOcAcreditadaPayload(Guid LineaOcId, decimal CantidadAcreditadaAcumulada);
