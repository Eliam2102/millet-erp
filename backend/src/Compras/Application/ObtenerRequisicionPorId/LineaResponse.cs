namespace Millet.Compras.Application.ObtenerRequisicionPorId;

/// <summary>
/// DTO de una línea dentro de <see cref="RequisicionResponse"/> (B.0).
/// Incluye el estado de cubrimiento decompuesto: <see cref="CantDeAlmacen"/>,
/// <see cref="CantDeCompra"/>, <see cref="CantRecibida"/> y la
/// <see cref="CantPendiente"/> computada para que el frontend pinte
/// <c>&lt;CubrimientoBar&gt;</c> sin lógica duplicada.
///
/// <para>
/// El precio se expone plano (<c>Monto</c> + <c>Moneda</c>) en lugar
/// de un objeto anidado para alinear con el shape del comando
/// <c>AgregarLineaCommand</c> (consistencia FE).
/// </para>
/// </summary>
public sealed record LineaResponse(
    Guid Id,
    short Posicion,
    Guid ArticuloId,
    // Etiqueta del artículo resuelta en backend (ADR-0042 addendum). Nullable →
    // el FE cae al id si no resuelve. Mapster las deja en null; el handler las
    // puebla tras el map (batch IArticuloReadPort).
    string? ArticuloClave,
    string? ArticuloNombre,
    decimal Cantidad,
    string UnidadMedida,
    decimal PrecioEstimadoMonto,
    string PrecioEstimadoMoneda,
    Guid? CuentaContableId,
    Guid? CentroCostoId,
    // Etiqueta del CC-Máquina (Dim3) resuelta en backend por IDim3ReadPort (PR1):
    // batch, SIN filtro de alcance e incluyendo inactivas (ADR-0050 "ver ≠ elegir";
    // ADR-0049). Mapster las deja en null; el handler las puebla tras el map. El FE
    // cae a "No catalogado" si no resuelve (id histórico sin fila en Dim3).
    string? CentroCostoClave,
    string? CentroCostoNombre,
    string? Proyecto,
    DateOnly? FechaRequerida,
    string? Notas,
    decimal CantDeAlmacen,
    decimal CantDeCompra,
    decimal CantRecibida,
    decimal CantPendiente,
    /// <summary>
    /// Cantidad ya <b>entregada</b> del almacén contra esta línea (suma
    /// de movimientos tipo <c>SalidaConsumo</c> con <c>RqId = X</c>,
    /// agrupados por <c>ArticuloId</c>). Permite al FE de salidas
    /// mostrar lo entregado y calcular pendiente correctamente.
    ///
    /// <para>Nota de terminología: "entregar" = salida del almacén al
    /// solicitante. NO confundir con "surtir" que en el negocio se
    /// refiere a recibir mercancía del proveedor (vía OC + recepción).</para>
    /// </summary>
    decimal CantEntregadoDeAlmacen,
    /// <summary>
    /// Cantidad pendiente de <b>entregar</b> al solicitante =
    /// <see cref="CantDeAlmacen"/> − <see cref="CantEntregadoDeAlmacen"/>,
    /// clamped a ≥ 0. La UI de salidas usa esto como default cantidad
    /// a entregar en el sheet "Nueva salida con RQ".
    /// </summary>
    decimal CantPendienteEntregar);
