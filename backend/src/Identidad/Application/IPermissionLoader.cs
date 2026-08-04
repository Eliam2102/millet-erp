namespace Millet.Identidad.Application;

/// <summary>
/// Carga los permisos efectivos de un usuario en una empresa específica.
/// "Efectivos" = unión de los permisos de todos los roles activos del usuario
/// activo en esa empresa, filtrando por <c>Usuario.Activo</c> y
/// <c>Rol.Activo</c>.
///
/// <para>
/// Llamado por el <c>PermissionAuthorizationHandler</c> en cache miss. El
/// resultado se cachea en <c>IPermissionCache</c> con TTL 5 min (ADR-0007).
/// </para>
/// </summary>
public interface IPermissionLoader
{
    /// <summary>
    /// Devuelve los códigos de permiso (strings <c>modulo.recurso.accion</c>)
    /// que el usuario tiene en la empresa indicada. Lista vacía si el usuario
    /// no está activo, no tiene asignaciones, o todos sus roles están
    /// desactivados.
    /// </summary>
    Task<IReadOnlyCollection<string>> LoadForUserInEmpresaAsync(
        Guid userId,
        Guid empresaId,
        CancellationToken cancellationToken = default);
}
