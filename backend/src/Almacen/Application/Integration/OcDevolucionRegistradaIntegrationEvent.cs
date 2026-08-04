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
    IReadOnlyList<LineaDevolucionProveedorPayload> Lineas)
    : IntegrationEvent("almacen.oc_devolucion.registrada.v1", EmpresaId, OcurridoEn);

/// <summary>
/// GAP-5 (verificación e2e 2026-07-15): <c>LineaOcId</c> es la línea de
/// OC afectada, resuelta por artículo contra la OC de origen vía
/// <c>IComprasOcReadPort</c> al registrar la salida (mismo criterio que
/// la recepción). Compras la usa para decrementar
/// <c>CantidadRecibida</c>; NULL cuando no hay OC de origen o el
/// artículo no matchea (Compras omite la línea).
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
