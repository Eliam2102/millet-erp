namespace Millet.Compras.Application.Oc.ObtenerOrdenCompraPorId;

/// <summary>
/// DTO con los datos de una línea de la <see cref="Domain.Oc.OrdenCompra"/>
/// para el response de <c>GET /api/v1/compras/ordenes/{id}</c>. Mirror
/// directo de <see cref="Domain.Oc.LineaOrdenCompra"/> con todos los
/// campos que el frontend necesita para renderizar el editor de
/// líneas (UF2-PR3) y los lectores read-only.
///
/// <para>
/// <b>Campos importantes</b>:
/// <list type="bullet">
///   <item><see cref="SubtotalLinea"/>: computed en el agregado
///   (<c>(Cantidad * PrecioUnitario) - Descuento.Aplicar(...)</c>) —
///   el frontend NO lo recalcula. Si el descuento de línea cambia
///   client-side, el caller debe re-fetchear o esperar al server
///   roundtrip.</item>
///   <item><see cref="RequisicionId"/>: <c>null</c> si la línea es
///   manual (creada en una OC sin RQ previa). Si no es null, viene
///   de una RQ y la cantidad/artículo NO son editables (política
///   §4.2 — la RQ es la fuente de verdad de qué se pidió).</item>
///   <item><see cref="RequisicionFolio"/>: folio humano de la RQ origen
///   (p. ej. <c>MID2026-000123</c>), resuelto en el backend por
///   <c>ObtenerOrdenCompraPorIdHandler</c> con query directa intra-Compras
///   a <c>Requisiciones</c> (ADR-0042, matiz intra-módulo). <c>null</c> si
///   la línea es manual o si la RQ no resuelve (línea borrada, etc.); el
///   frontend cae al id en ese caso.</item>
///   <item><see cref="CantidadRecibida"/> y <see cref="CantidadFacturada"/>:
///   sub-estado por línea. Cuando ambos lleguen a <c>Cantidad</c>, la
///   línea contribuye a <c>SubEstadoRecepcion.Completa</c> y
///   <c>SubEstadoFacturacion.Completa</c> de la OC.</item>
/// </list>
/// </para>
/// </summary>
public sealed record LineaOrdenCompraResponse(
    Guid Id,
    int Posicion,
    Guid ArticuloId,
    // Etiqueta del artículo resuelta en backend (ADR-0042 addendum). Nullable →
    // el FE cae al id. Mapster las deja en null; el handler las puebla tras el
    // map (batch IArticuloReadPort).
    string? ArticuloClave,
    string? ArticuloNombre,
    string? DescripcionExtendida,
    decimal Cantidad,
    string UnidadMedida,
    decimal PrecioUnitario,
    decimal IvaImporte,
    decimal? RetencionIsr,
    decimal SubtotalLinea,
    Guid DepartamentoSolicitanteId,
    Guid? CentroCostoId,
    // Etiqueta del CC-Máquina (Dim3) resuelta por IDim3ReadPort (batch, SIN
    // filtro de alcance, incluye inactivas — ADR-0050/ADR-0049). Mapster deja
    // Clave/Nombre en null; el handler las puebla. El FE cae a "No catalogado".
    string? CentroCostoClave,
    string? CentroCostoNombre,
    Guid? RequisicionId,
    Guid? LineaRequisicionId,
    string? RequisicionFolio,
    DateTimeOffset? FechaEntregaLinea,
    decimal CantidadRecibida,
    decimal CantidadFacturada,
    string? TextoAdicional);
