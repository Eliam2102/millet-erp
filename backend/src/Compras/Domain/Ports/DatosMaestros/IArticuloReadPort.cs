namespace Millet.Compras.Domain.Ports.DatosMaestros;

/// <summary>
/// Puerto de lectura de Compras hacia el catálogo de artículos
/// (<c>compartido.articulos</c>). Resuelve en batch
/// <c>articuloId → (clave, nombre)</c> para enriquecer los DTOs de detalle
/// de requisición/OC con la etiqueta del artículo, sin que el cliente lea
/// el catálogo completo (ADR-0042).
///
/// <para>
/// Batch (no por-id) para evitar N+1 al pintar las líneas de un detalle: el
/// handler junta los <c>ArticuloId</c> distintos de las líneas y resuelve en
/// una sola consulta. Las claves no encontradas no aparecen en el
/// diccionario (el handler hace fallback al id). Reusable por los follow-ups
/// del mismo módulo (bandejas OC).
/// </para>
/// </summary>
public interface IArticuloReadPort
{
    Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
        IReadOnlyCollection<Guid> articuloIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// Proyección mínima de artículo para display (clave + nombre) más la
/// bandera <see cref="EsServicio"/> (GAP-9): los servicios no se reciben
/// en Almacén, así que las líneas de OC de servicio se excluyen del
/// sub-estado de Recepción. Default <c>false</c> para no romper callers
/// que solo necesitan la etiqueta.
/// </summary>
public sealed record ArticuloLectura(Guid Id, string Clave, string Nombre, bool EsServicio = false);
