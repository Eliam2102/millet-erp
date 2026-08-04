namespace Millet.Compras.Domain.Ports.Identidad;

/// <summary>
/// Puerto de lectura de Compras hacia el módulo Identidad. Resuelve, en
/// batch, <c>usuarioId → nombre</c> para enriquecer los DTOs de lectura de
/// requisiciones con el nombre del requisitante sin que el cliente necesite
/// leer el padrón completo de usuarios (ADR-0042).
///
/// <para>
/// Batch (no por-id) para evitar N+1 al pintar una página de bandeja: el
/// handler junta los <c>RequisitanteId</c> de la página y resuelve en una
/// sola consulta. Las claves no encontradas simplemente no aparecen en el
/// diccionario (el handler hace fallback al id).
/// </para>
/// </summary>
public interface IUsuarioReadPort
{
    Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresAsync(
        IReadOnlyCollection<Guid> usuarioIds,
        CancellationToken cancellationToken);
}
