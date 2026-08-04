namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Puerto de lectura del catálogo de sucursales (dueño: Administración —
/// <c>compartido.sucursales</c>). Lo consumen los validators del CRUD de
/// Cajas (CAJAS-PR1): el alcance de una caja solo admite sucursales
/// existentes y activas. Adapter real: <c>SucursalesReadAdapter</c> sobre
/// <c>CompartidoDbContext</c> (cero escritura, mismo precedente que
/// <see cref="ICanalesVentaReadPort"/>).
/// </summary>
public interface ISucursalesReadPort
{
    /// <summary>
    /// De los ids dados, cuáles NO existen como sucursal activa. Vacío = todos
    /// válidos. Una sola consulta para validar el replace-set completo.
    /// </summary>
    Task<IReadOnlyList<Guid>> FiltrarNoActivasAsync(
        IReadOnlyCollection<Guid> sucursalIds, CancellationToken cancellationToken);

    /// <summary>
    /// Zona horaria IANA de la sucursal (`[Decisión 12-8]`, CAJAS-PR3) —
    /// define el día de operación de las sesiones de caja. <c>null</c> si la
    /// sucursal no existe.
    /// </summary>
    Task<string?> ObtenerZonaHorariaAsync(Guid sucursalId, CancellationToken cancellationToken);
}
