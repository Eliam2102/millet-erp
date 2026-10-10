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
    decimal MontoTotalMxn);
