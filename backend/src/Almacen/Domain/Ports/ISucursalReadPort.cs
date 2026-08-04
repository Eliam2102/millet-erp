namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de lectura hacia Administración para obtener datos de
/// sucursal. Lo usa el agregado <c>Almacen</c> (F1-PR1) al validar la
/// jerarquía Sucursal → Almacén → Sub-almacén.
/// </summary>
public interface ISucursalReadPort
{
    Task<SucursalLectura?> ObtenerAsync(Guid sucursalId, CancellationToken cancellationToken);
}

public sealed record SucursalLectura(
    Guid Id,
    string Clave,
    string Nombre,
    Guid EmpresaId,
    bool EsActiva);
