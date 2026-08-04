namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de lectura hacia DatosMaestros para obtener datos del proveedor.
/// Lo usa el sub-flujo 8.B (Devolución a proveedor, F6-PR1) para
/// referenciar el proveedor destino del movimiento.
/// </summary>
public interface IProveedorReadPort
{
    Task<ProveedorLectura?> ObtenerAsync(Guid proveedorId, CancellationToken cancellationToken);

    /// <summary>
    /// Resuelve en <b>batch</b> <c>proveedorId → proveedor</c> para
    /// enriquecer DTOs de lectura con la razón social sin N+1 (ADR-0042,
    /// mismo contrato que <see cref="IArticuloReadPort.ObtenerPorIdsAsync"/>).
    /// La bandeja de devoluciones a proveedor junta los <c>ProveedorId</c>
    /// distintos de la página y resuelve en una sola consulta. Las claves
    /// no encontradas simplemente no aparecen en el diccionario (el
    /// consumidor hace fallback al id).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, ProveedorLectura>> ObtenerPorIdsAsync(
        IReadOnlyCollection<Guid> proveedorIds,
        CancellationToken cancellationToken);
}

public sealed record ProveedorLectura(
    Guid Id,
    string Clave,
    string RazonSocial,
    string? Rfc,
    bool EsActivo);
