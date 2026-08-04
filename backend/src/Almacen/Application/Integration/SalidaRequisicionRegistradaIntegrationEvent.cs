using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.Integration;

/// <summary>
/// EventType <c>almacen.salida_requisicion.registrada.v1</c>. Publicado
/// al outbox cuando una salida (variante A normal o B vale) pasa a
/// estado Registrado (F4-PR1, F5-PR1). Consumidores:
/// <list type="bullet">
///   <item><b>Contabilidad</b> — genera póliza de salida con cargo a
///   centro de costo / proyecto.</item>
///   <item><b>Requisiciones (Compras)</b> — actualiza
///   <c>CantidadSurtida</c> en la línea de RQ (variante A); evalúa si
///   la RQ pasa a estado Surtida.</item>
/// </list>
///
/// <para>
/// El payload incluye una bandera <c>EsPorVale</c> que diferencia
/// variante A (con RQ) de variante B (vale firmado, sin RQ todavía).
/// </para>
/// </summary>
// Salida-por-línea C3: se RETIRÓ SubAlmacenId. El único consumidor (Compras)
// nunca lo leyó — su espejo SalidaRequisicionRegistradaAlmacenPayload no tiene
// el campo (campo muerto en el cable, igual que recepción antes de 6c). La
// deserialización del consumidor (PropertyNameCaseInsensitive, sin
// UnmappedMemberHandling.Disallow) tolera mensajes legacy en vuelo que aún lo
// traigan: la propiedad desconocida se ignora. CxP no consume salidas.
public sealed record SalidaRequisicionRegistradaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid SalidaId,
    string FolioSalida,
    DateOnly FechaMovimiento,
    Guid? RqId,
    bool EsPorVale,
    Guid? PersonaDestinatariaId,
    string? ValeBlobRef,
    IReadOnlyList<LineaSalidaPayload> Lineas)
    : IntegrationEvent("almacen.salida_requisicion.registrada.v1", EmpresaId, OcurridoEn);

public sealed record LineaSalidaPayload(
    Guid LineaSalidaId,
    Guid ArticuloId,
    string UnidadMedida,
    decimal Cantidad,
    decimal CostoUnitarioMxn,
    decimal MontoTotalMxn,
    Guid? CentroCostoId,
    Guid? ProyectoId,
    // ADR-0043: línea de RQ que surte esta línea de salida. NULL en vale
    // (sin RQ). Compras la usa para acumular CantidadEntregada por línea.
    Guid? LineaRqId = null,
    // Almacén-por-línea PR5: bin nivel 4 del que sale la mercancía. Aditivo
    // (sin bump de versión) — ningún consumidor actual lo espeja.
    //
    // NULLABLE a propósito: la ubicación es opcional en la captura de salida
    // hoy — los validators server-side no la exigen (a diferencia de las
    // entradas, que sí: RegistrarRecepcionConFacturaValidator). Sólo el Zod
    // del frontend la obliga, así que un cliente del API puede omitirla y el
    // trigger la enruta a la ÚNICA. Se publica el valor CRUDO: null significa
    // "el almacenista no eligió bin", no se sustituye por el fallback del
    // sistema. PR6 lo endurecerá tras el backfill + SET NOT NULL.
    Guid? UbicacionId = null);
