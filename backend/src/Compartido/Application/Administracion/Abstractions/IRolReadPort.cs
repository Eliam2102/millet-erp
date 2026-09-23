namespace Millet.Administracion.Application.Abstractions;

/// <summary>
/// Puerto de lectura cross-módulo para consultar Roles del sistema (F1-ADM-01.4).
/// La entidad <c>Rol</c> vive en el módulo Identidad (tabla <c>identidad.roles</c>);
/// como Compartido no referencia Identidad (evita dependencia circular),
/// este puerto se declara aquí y se implementa en <c>Identidad.Infrastructure.PublicAdapters</c>.
/// </summary>
public interface IRolReadPort
{
    /// <summary>
    /// Verifica si el rol existe y está activo.
    /// </summary>
    Task<bool> ExisteActivoAsync(Guid rolId, CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene un diccionario de [RolId -> Nombre] para los roles indicados.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresPorIdsAsync(
        IEnumerable<Guid> rolIds, CancellationToken cancellationToken);
}
