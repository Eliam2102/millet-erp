using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.Integration;

/// <summary>
/// EventType <c>almacen.oc_recepcion.registrada.v1</c>. Publicado al
/// outbox por Almacén cuando una <see cref="Almacen.Domain.Movimientos.MovimientoInventario"/>
/// tipo <c>EntradaCompra</c> pasa a estado Registrado contra una OC
/// autorizada (F2-PR2, Variante A; F3-PR1, Variante B).
///
/// <para>
/// Consumidores (CLAUDE.md §"Triada"):
/// <list type="bullet">
///   <item><b>Compras</b>: incrementa <c>CantidadRecibida</c> en la línea
///   de OC; evalúa sub-estado <c>Recepción</c> de la OC.</item>
///   <item><b>CxP F5-PR1</b>: si la recepción es Variante B
///   (<c>FacturaPendiente = true</c>), CxP marca su lookup como
///   "esperando factura". Variante A (con CFDI ya vinculado) puede ser
///   trigger para conciliar tres vías.</item>
/// </list>
/// </para>
///
/// <para>
/// La <c>idempotencia</c> en consumidores se logra correlacionando por
/// <see cref="RecepcionId"/> (id del movimiento) — campo único de
/// referencia para detectar reprocesos.
/// </para>
/// </summary>
public sealed record OcRecepcionRegistradaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid RecepcionId,
    string FolioRecepcion,
    Guid OrdenCompraId,
    DateOnly FechaMovimiento,
    bool FacturaPendiente,
    Guid? CfdiRecibidoId,
    // Folio fiscal (UUID SAT, mayúsculas) capturado del impreso cuando el
    // CFDI aún no está en el repositorio de CxP. Permite a CxP correlacionar
    // la recepción con el CfdiRecibido cuando el XML llegue por su canal.
    string? CfdiUuidFiscal,
    string? Observaciones,
    IReadOnlyList<LineaRecepcionPayload> Lineas)
    : IntegrationEvent("almacen.oc_recepcion.registrada.v1", EmpresaId, OcurridoEn);

/// <summary>
/// Detalle por línea de la recepción: cantidad recibida + costo
/// snapshot (precio de OC) + vínculo opcional a la línea de OC original
/// (NULL en variante B antes de conciliación).
///
/// <para>Almacén-por-línea PR4: se agrega <see cref="UbicacionId"/> — el bin
/// N4 real donde entró la línea. Cambio <b>aditivo</b> (sin bump de versión):
/// los consumidores actuales ignoran el campo nuevo. Es no-nullable porque la
/// recepción exige ubicación por línea desde ADR-0047 C7.2b.</para>
///
/// <para>Almacén-por-línea 6c: el <c>SubAlmacenId</c> de cabecera se retiró del
/// evento. El código siempre tuvo <b>DOS</b> consumidores con espejo —CxP y
/// Compras—; ambos dejaron de declararlo (CxP en 6b-1, Compras en 6c) y ninguno
/// lo leía. El sub-almacén se deriva por línea del bin N4
/// (<see cref="UbicacionId"/>).</para>
/// </summary>
public sealed record LineaRecepcionPayload(
    Guid LineaRecepcionId,
    Guid? LineaOcId,
    Guid ArticuloId,
    string UnidadMedida,
    decimal Cantidad,
    decimal CostoUnitarioMxn,
    decimal MontoTotalMxn,
    Guid UbicacionId);
