namespace Millet.Almacen.Domain.Saldos;

/// <summary>
/// Saldo materializado por <c>(ubicacion_id, articulo_id)</c> (ADR-0047, PR2 —
/// antes <c>(sub_almacen_id, articulo_id)</c>). Tabla
/// <c>almacen.saldos_inventario</c> — actualizada por el trigger
/// <c>tg_movimientos_actualizar_saldo</c> en la misma transacción del INSERT
/// del movimiento. La existencia se cuenta en Nivel 4 (ubicación).
///
/// <para>
/// <b><see cref="SubAlmacenId"/> denormalizado</b>: se conserva junto a
/// <see cref="UbicacionId"/> (el trigger lo escribe; el puerto de saldos, las
/// queries y la asignación lo usan). Es load-bearing — NO se elimina (ADR-0047).
/// </para>
///
/// <para>
/// <b>Invariantes</b>:
/// <list type="bullet">
///   <item><see cref="Cantidad"/> >= 0 (CHECK).</item>
///   <item><see cref="CantidadDisponible"/> es columna generada
///   <c>STORED</c> = <see cref="Cantidad"/> (PR4: sin reservas, disponible == físico).</item>
///   <item><see cref="ValorInventarioMxn"/> es columna generada
///   <c>STORED</c> = <see cref="Cantidad"/> * <see cref="CostoPromedioMxn"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Costo promedio ponderado</b> (A3): se actualiza al recibir una
/// entrada con la fórmula <c>nuevo_promedio = (cantidad_anterior *
/// promedio_anterior + cantidad_entrante * costo_entrante) /
/// (cantidad_anterior + cantidad_entrante)</c>. El trigger PG lo
/// calcula atomicamente.
/// </para>
/// </summary>
public sealed class SaldoInventario
{
    public Guid UbicacionId { get; private set; }
    public Guid SubAlmacenId { get; private set; }
    public Guid ArticuloId { get; private set; }
    public decimal Cantidad { get; private set; }
    public decimal CantidadDisponible { get; private set; }
    public decimal CostoPromedioMxn { get; private set; }
    public decimal ValorInventarioMxn { get; private set; }
    public DateTimeOffset UltimaActualizacionAt { get; private set; }
    public Guid? UltimoMovimientoId { get; private set; }

    private SaldoInventario() { }

    /// <summary>
    /// Constructor disponible para tests del dominio. En runtime real,
    /// las filas las crea el trigger PG (UPSERT) al insertar el primer
    /// movimiento con un nuevo par <c>(ubicacion, articulo)</c>.
    /// </summary>
    public SaldoInventario(
        Guid ubicacionId,
        Guid subAlmacenId,
        Guid articuloId,
        decimal cantidad,
        decimal costoPromedioMxn)
    {
        UbicacionId = ubicacionId;
        SubAlmacenId = subAlmacenId;
        ArticuloId = articuloId;
        Cantidad = cantidad;
        CostoPromedioMxn = costoPromedioMxn;
        CantidadDisponible = cantidad;
        ValorInventarioMxn = cantidad * costoPromedioMxn;
        UltimaActualizacionAt = DateTimeOffset.UtcNow;
    }
}
