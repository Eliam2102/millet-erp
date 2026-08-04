namespace Millet.Almacen.Domain.Movimientos;

/// <summary>
/// Almacén-por-línea PR6a: proyección keyless del sub-almacén de un movimiento,
/// derivado de la ubicación (bin N4) de su primera línea — <b>una fila por
/// movimiento</b>.
///
/// <para>En PostgreSQL mapea a la vista <c>almacen.v_movimiento_sub_almacen</c>
/// (<c>DISTINCT ON (movimiento_id)</c>). En EF InMemory (unit tests) se resuelve
/// vía <c>ToInMemoryQuery</c> con la misma derivación en LINQ — el proveedor
/// elige. Sustituye a la columna de cabecera <c>MovimientoInventario.SubAlmacenId</c>,
/// retirada en PR6a. Que sea bien-definido (un solo sub por movimiento) lo
/// garantiza el invariante <c>MOVIMIENTO_MULTI_SUBALMACEN</c> del trigger.</para>
///
/// <para>Ctor primario con props get-only: EF materializa por constructor
/// (enlaza por nombre de parámetro) en ambos proveedores.</para>
/// </summary>
public sealed class MovimientoSubAlmacen(Guid movimientoId, Guid subAlmacenId)
{
    public Guid MovimientoId { get; } = movimientoId;
    public Guid SubAlmacenId { get; } = subAlmacenId;
}
