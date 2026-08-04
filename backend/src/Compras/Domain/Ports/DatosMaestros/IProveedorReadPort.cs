namespace Millet.Compras.Domain.Ports.DatosMaestros;

/// <summary>
/// Puerto de lectura de Compras hacia el catálogo de proveedores
/// (<c>compartido.proveedores</c>). Resuelve en batch
/// <c>proveedorId → (clave, razón social)</c> para enriquecer los DTOs de
/// detalle de requisición/OC con la etiqueta del proveedor, sin que el
/// cliente lea el catálogo completo (ADR-0042).
///
/// <para>
/// Batch (no por-id) para evitar N+1 y para que los follow-ups del mismo
/// módulo (bandejas OC) lo reusen sobre una página. Las claves no
/// encontradas no aparecen en el diccionario (el handler hace fallback al id).
/// </para>
/// </summary>
public interface IProveedorReadPort
{
    Task<IReadOnlyDictionary<Guid, ProveedorLectura>> ObtenerPorIdsAsync(
        IReadOnlyCollection<Guid> proveedorIds,
        CancellationToken cancellationToken);
}

/// <summary>Proyección mínima de proveedor para display (clave + razón social).</summary>
public sealed record ProveedorLectura(Guid Id, string Clave, string RazonSocial);
