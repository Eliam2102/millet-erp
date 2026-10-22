namespace Millet.Compras.Application.Almacen;

// ============================================================================
// Contratos ESPEJO de los eventos de Almacén que Compras consume.
//
// Mismo patrón que CxP/Almacen: payloads locales para que el listener
// del Service Bus pueda deserializar sin acoplar bounded contexts. La
// compatibilidad de contrato se mantiene por convención de versión en el
// EventType (vN); cambios incompatibles bumpean a v(N+1) y ambos lados
// migran juntos.
// ============================================================================

/// <summary>
/// Espejo de <c>almacen.oc_recepcion.registrada.v1</c>. Compras lo
/// consume para incrementar <c>CantidadRecibida</c> por línea de OC y
/// recalcular el sub-estado Recepción de la orden. Idempotencia por
/// <c>RecepcionId</c>.
/// </summary>
// Almacén-por-línea 6c: se retiró SubAlmacenId. Compras nunca lo leyó (era
// campo muerto en el espejo, igual que en CxP antes de 6b). Su deserialización
// (PropertyNameCaseInsensitive, sin UnmappedMemberHandling.Disallow) tolera que
// un mensaje legacy en vuelo aún lo traiga: la propiedad desconocida se ignora.
public sealed record OcRecepcionRegistradaAlmacenPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid RecepcionId,
    string FolioRecepcion,
    Guid OrdenCompraId,
    DateOnly FechaMovimiento,
    bool FacturaPendiente,
    Guid? CfdiRecibidoId,
    string? Observaciones,
    IReadOnlyList<LineaRecepcionAlmacenPayload> Lineas);

/// <summary>
/// Detalle por línea de la recepción. <c>LineaOcId</c> puede ser null en
/// variante B antes de conciliación; en ese caso Compras lo resuelve por
/// (OC, artículo) único o falla visible.
/// </summary>
public sealed record LineaRecepcionAlmacenPayload(
    Guid LineaRecepcionId,
    Guid? LineaOcId,
    Guid ArticuloId,
    string UnidadMedida,
    decimal Cantidad,
    decimal CostoUnitarioMxn,
    decimal MontoTotalMxn,
    // Aditivos (86e3pc50v): recibido neto acumulado de la línea de OC tras este evento, en unidad de la OC, calculado por Almacén.
    // Obsoleto (86e3tab1k): ya no se llena; en piezas no hay tolerancia.
    decimal? ToleranciaPorcentaje = null,
    decimal? CantidadRecibidaAcumulada = null);

/// <summary>
/// Espejo de <c>almacen.salida_requisicion.registrada.v1</c> (ADR-0043).
/// Compras lo consume para acumular <c>CantidadEntregada</c> por línea de RQ
/// (canal de entrega Almacén→Compras). Idempotencia por el id del evento.
/// <c>RqId == null</c> en salida por vale (sin RQ) → no hay entrega que
/// proyectar; solo se marca el evento como procesado.
/// </summary>
public sealed record SalidaRequisicionRegistradaAlmacenPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid SalidaId,
    string FolioSalida,
    Guid? RqId,
    bool EsPorVale,
    IReadOnlyList<LineaSalidaAlmacenPayload> Lineas);

/// <summary>
/// Detalle por línea de la salida. <c>LineaRqId</c> es la línea de RQ que
/// esta línea surte; <c>null</c> cuando la salida no proviene de una RQ
/// (vale) — en ese caso Compras la omite.
/// </summary>
public sealed record LineaSalidaAlmacenPayload(
    Guid LineaSalidaId,
    Guid ArticuloId,
    decimal Cantidad,
    Guid? LineaRqId);

/// <summary>
/// Espejo de <c>almacen.oc_devolucion.registrada.v1</c> (GAP-5 de la
/// verificación e2e 2026-07-15). Compras lo consume para decrementar
/// <c>CantidadRecibida</c> por línea de OC (tabla canónica de la triada).
/// <c>LineaOcId</c> es aditivo — Almacén lo resuelve por artículo contra
/// la OC al registrar la salida; NULL cuando no se pudo resolver (Compras
/// omite la línea y solo marca el evento como procesado).
/// </summary>
public sealed record OcDevolucionRegistradaAlmacenPayload(
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
    IReadOnlyList<LineaDevolucionAlmacenPayload> Lineas);

public sealed record LineaDevolucionAlmacenPayload(
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
/// Espejo de <c>almacen.recepcion_activo.anulada.v1</c>. Compras lo consume para fijar
/// <c>CantidadRecibida</c> de la línea de OC al acumulado (<c>CantidadRecibidaAcumulada</c>) que calcula Almacén.
/// </summary>
public sealed record RecepcionActivoAnuladaAlmacenPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid RecepcionActivoId,
    Guid RecepcionId,
    Guid OrdenCompraId,
    Guid LineaOcId,
    decimal Cantidad,
    decimal CantidadRecibidaAcumulada,
    string Motivo);
