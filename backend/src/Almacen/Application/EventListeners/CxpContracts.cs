namespace Millet.Almacen.Application.EventListeners;

// ============================================================================
// Contratos ESPEJO de los eventos de CxP que Almacén consume (F3-PR1).
//
// Estos records reflejan los payloads publicados por CxP (vive en
// Millet.CuentasPorPagar.Application.Integration). Definimos copias locales
// para mantener el bounded context cerrado — Almacén NO referencia el
// proyecto CxP. La compatibilidad de contrato se mantiene por la convención
// de versión en el EventType (vN); cambios incompatibles bumpean a v(N+1)
// y ambos lados migran juntos.
//
// Convención CLAUDE.md §"Triada": el listener desempaqueta el JSON del
// outbox bajando del Service Bus topic `cuentas-por-pagar-events`.
// ============================================================================

/// <summary>
/// Espejo del payload de <c>cuentas_por_pagar.factura.registrada.v1</c>.
/// Almacén suscribe para conciliar recepción Variante B con la factura
/// que llega.
/// </summary>
public sealed record FacturaProveedorRegistradaPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaProveedorId,
    Guid OrdenCompraId,
    decimal TotalFactura,
    IReadOnlyList<LineaFacturadaPayload> Lineas);

public sealed record LineaFacturadaPayload(
    Guid LineaFacturaId,
    Guid? LineaOcId,
    decimal Cantidad,
    decimal Importe);

/// <summary>
/// Espejo del payload de
/// <c>cuentas_por_pagar.factura.diferencia-precio-detectada.v1</c>.
/// Almacén suscribe para generar movimiento <c>AjustePrecioFactura</c>
/// (A11) y ajustar costo del inventario remanente.
/// </summary>
public sealed record DiferenciaPrecioFacturaDetectadaPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaProveedorId,
    Guid OrdenCompraId,
    Guid ArticuloId,
    decimal CantidadFacturada,
    decimal PrecioFacturaUnitarioMxn,
    decimal PrecioOcUnitarioMxn,
    decimal DiferenciaUnitarioMxn,
    decimal MontoDiferenciaTotalMxn);

/// <summary>
/// Espejo del payload de <c>cuentas_por_pagar.cfdi.ingresado.v1</c>.
/// Almacén suscribe para el enlace diferido de la recepción variante A
/// (§5.4): recepciones registradas con folio fiscal capturado a mano
/// (<c>cfdi_uuid_fiscal</c>) backfillean su <c>CfdiRecibidoId</c> cuando
/// el XML entra al repositorio de CxP por cualquier canal.
/// </summary>
public sealed record CfdiRecibidoIngresadoPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid CfdiRecibidoId,
    string UuidCfdi,
    string RfcEmisor);

// El mensaje de Service Bus lleva el payload del evento concreto como
// body JSON; el EventType (ej. `cuentas_por_pagar.factura.registrada.v1`)
// viaja en `Subject` y la app property `EventType`; el `MessageId` es
// el id del outbox entry (lo usamos como EventoId para dedupe en
// `eventos_procesados`). El listener no necesita envelope: deserializa
// el body directamente como el payload concreto según el EventType.
