namespace Millet.CuentasPorPagar.Application.EventListeners;

// ============================================================================
// Contratos ESPEJO de los eventos de Compras y Almacén que CxP consume.
//
// Mismo patrón que Almacén F3-PR1: definimos copias locales para que el
// listener del Service Bus pueda deserializar sin acoplar bounded
// contexts cruzados. Cuando lleguen real-time desde el broker, el
// dispatcher mapea EventType → payload + Command.
//
// La compatibilidad de contrato se mantiene por la convención de versión
// en el EventType (vN); cambios incompatibles bumpean a v(N+1) y ambos
// lados migran juntos.
// ============================================================================

/// <summary>
/// Espejo de <c>compras.orden-compra.autorizada.v1</c>. Informativo
/// para CxP — la OC ya es facturable.
/// </summary>
public sealed record OcAutorizadaPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid OrdenCompraId,
    string Folio,
    Guid CompradorTitularId,
    DateTimeOffset FechaContabilizacion);

/// <summary>
/// Espejo de <c>compras.orden-compra.cancelada.v1</c>. CxP debe alertar
/// al Auxiliar si hay facturas en captura asociadas a la OC.
/// </summary>
public sealed record OcCanceladaPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid OrdenCompraId,
    string Folio,
    Guid CompradorTitularId,
    Guid UsuarioCanceladorId,
    Guid MotivoCancelacionId,
    string? MotivoCancelacionTexto);

/// <summary>
/// Espejo de <c>almacen.oc_recepcion.registrada.v1</c>. CxP proyecta
/// localmente para que <c>IAlmacenRecepcionReadPort</c> resuelva sin
/// query cross-DbContext.
/// </summary>
public sealed record OcRecepcionRegistradaPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid RecepcionId,
    string FolioRecepcion,
    Guid OrdenCompraId,
    // Almacén-por-línea 6b: el espejo YA NO declara SubAlmacenId (era
    // almacenamiento muerto: se persistía en RecepcionOcLocal pero nadie lo
    // leía). Almacén sigue emitiéndolo hasta 6c; System.Text.Json ignora la
    // propiedad extra al deserializar (JsonOpts sin UnmappedMemberHandling.Disallow).
    DateOnly FechaMovimiento,
    bool FacturaPendiente,
    Guid? CfdiRecibidoId,
    string? Observaciones,
    IReadOnlyList<LineaRecepcionPayload> Lineas);

public sealed record LineaRecepcionPayload(
    Guid LineaRecepcionId,
    Guid? LineaOcId,
    Guid ArticuloId,
    string UnidadMedida,
    decimal Cantidad,
    decimal CostoUnitarioMxn,
    decimal MontoTotalMxn);

/// <summary>
/// Espejo de <c>almacen.oc_devolucion.registrada.v1</c>. CxP genera
/// <c>NotaCargo</c> en Borrador asociada a esta devolución, lista para
/// autorización + aplicación. La NC fiscal del proveedor (tipo 03)
/// cierra el ciclo vía <c>NotaCreditoFiscalDevolucionRecibidaEvent</c>.
/// </summary>
public sealed record OcDevolucionRegistradaPayload(
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
    IReadOnlyList<LineaDevolucionProveedorPayload> Lineas);

public sealed record LineaDevolucionProveedorPayload(
    Guid LineaDevolucionId,
    Guid? LineaRecepcionOrigenId,
    Guid ArticuloId,
    string UnidadMedida,
    decimal Cantidad,
    decimal CostoUnitarioMxn,
    decimal MontoTotalMxn);

// ============================================================================
// F9-PR1: contratos espejo de los 4 eventos que Tesorería emite y CxP
// suscribe. Mismos patrones que Compras/Almacén — payloads locales para
// que el listener deserialice sin acoplar bounded contexts.
// ============================================================================

/// <summary>Espejo de <c>tesoreria.pago-factura-proveedor.aplicado.v1</c>.</summary>
public sealed record PagoFacturaProveedorPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaProveedorId,
    Guid PagoId,
    decimal Monto,
    string Moneda,
    DateOnly FechaPago,
    string? MetodoPago,
    string? ReferenciaBancaria);

/// <summary>Espejo de <c>tesoreria.pago-prestamo-viaticos.aplicado.v1</c> (GI-PR3).</summary>
public sealed record PagoPrestamoViaticosAplicadoPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid SolicitudViaticosId,
    Guid PagoId,
    decimal MontoPagado,
    string Moneda,
    DateOnly FechaPago,
    string? ReferenciaBancaria);

/// <summary>Espejo de <c>tesoreria.pago-factura-proveedor.revertido.v1</c>.</summary>
public sealed record PagoFacturaProveedorRevertidoPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaProveedorId,
    Guid PagoOriginalId,
    decimal MontoRevertido,
    string Moneda,
    DateOnly FechaReversa,
    string Motivo);

/// <summary>Espejo de <c>tesoreria.repp-proveedor.recibido.v1</c>.</summary>
public sealed record ReppProveedorRecibidoPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaProveedorId,
    string UuidComplementoPago,
    DateTimeOffset FechaComplemento);

/// <summary>Espejo de <c>tesoreria.cancelacion-pasivo.solicitada.v1</c>.</summary>
public sealed record CancelacionPasivoSolicitadaPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaProveedorId,
    Guid UsuarioSolicitanteId,
    string Motivo);
