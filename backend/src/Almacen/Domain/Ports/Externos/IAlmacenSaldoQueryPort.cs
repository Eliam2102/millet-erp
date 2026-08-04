namespace Millet.Almacen.Domain.Ports.Externos;

/// <summary>
/// <b>Open Host Service</b> del módulo Almacén (F2-PR2, 01-diseno §6.2).
/// Otros módulos NUNCA leen directamente <c>almacen.saldos_inventario</c>.
/// Consumen este puerto inyectado para mantener el bounded context cerrado
/// — el schema interno de Almacén puede evolucionar sin romper consumidores.
///
/// <para>
/// <b>Consumidores</b>: Compras (bifurcación de RQ: consulta disponibilidad por
/// almacén para el split DeAlmacen/DeCompra), CxP (validación de disponibilidad
/// pre-pago, futuro).
/// </para>
///
/// <para>
/// El adapter productivo vive en
/// <c>Millet.Almacen.Infrastructure.PublicAdapters.AlmacenSaldoQueryAdapter</c>
/// — read-only sobre la tabla materializada.
/// </para>
/// </summary>
public interface IAlmacenSaldoQueryPort
{
    /// <summary>
    /// Saldo de un artículo en un sub-almacén específico. <c>null</c>
    /// si no hay fila (el par nunca ha tenido movimiento).
    /// </summary>
    Task<SaldoArticulo?> ConsultarAsync(
        Guid articuloId,
        Guid subAlmacenId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Suma de saldos del artículo a través de todos los sub-almacenes
    /// (visión global del stock). Devuelve cero si no hay filas.
    /// </summary>
    Task<SaldoArticulo> ConsultarTotalArticuloAsync(
        Guid articuloId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Disponibilidad de un artículo agregada a nivel <b>almacén</b> — suma los
    /// saldos de todas las ubicaciones (Nivel 4) bajo los sub-almacenes de ese
    /// almacén. Es el rollup que Compras necesita: la RQ guarda
    /// <c>AlmacenDestinoId</c> (almacén padre), no ubicación. Devuelve cero si
    /// no hay filas. Almacén resuelve la jerarquía internamente — el consumidor
    /// nunca toca <c>almacen.saldos_inventario</c> directo (ADR-0047, PR2).
    /// </summary>
    Task<DisponibilidadArticuloAlmacen> ConsultarDisponibilidadPorAlmacenAsync(
        Guid almacenId,
        Guid articuloId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Disponibilidad de un artículo agregada a nivel <b>sucursal</b> (N1) — suma los
    /// saldos de todas las ubicaciones bajo TODOS los almacenes de la sucursal (join
    /// extra sub-almacenes→almacenes por <c>SucursalId</c>). Lo usa el motor de reorden
    /// (ADR-0047 PR5.B) cuando la config vive a Nivel 1. Devuelve cero si no hay filas.
    /// </summary>
    Task<DisponibilidadArticuloSucursal> ConsultarDisponibilidadPorSucursalAsync(
        Guid sucursalId,
        Guid articuloId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Disponibilidad agregada de un artículo en un almacén (rollup sobre las
/// ubicaciones de sus sub-almacenes). <see cref="CantidadDisponible"/> =
/// <see cref="Cantidad"/> (PR4: sin reservas), sumadas.
/// </summary>
public sealed record DisponibilidadArticuloAlmacen(
    Guid AlmacenId,
    Guid ArticuloId,
    decimal Cantidad,
    decimal CantidadDisponible);

/// <summary>
/// Disponibilidad agregada de un artículo en una sucursal (rollup sobre las
/// ubicaciones de todos los almacenes de la sucursal). <see cref="CantidadDisponible"/>
/// = <see cref="Cantidad"/> (PR4: sin reservas), sumadas.
/// </summary>
public sealed record DisponibilidadArticuloSucursal(
    Guid SucursalId,
    Guid ArticuloId,
    decimal Cantidad,
    decimal CantidadDisponible);

/// <summary>
/// Proyección read-only de un saldo. Inmutable. Las cantidades son
/// snapshot del momento de la consulta — para reservar / consumir,
/// usar <c>IAlmacenReservaPort</c> (F3-PR2).
/// </summary>
public sealed record SaldoArticulo(
    Guid ArticuloId,
    Guid? SubAlmacenId,
    decimal Cantidad,
    decimal CantidadDisponible,
    decimal CostoPromedioMxn,
    decimal ValorInventarioMxn);
