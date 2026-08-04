namespace Millet.SharedKernel.Application;

/// <summary>
/// Permisos efectivos del principal del request actual (humano o service
/// principal), consultables desde query/command handlers para scoping fino de
/// datos — a diferencia del RBAC de endpoint (<c>RequirePermission</c>), que
/// solo decide 200/403. Primera consumidora: la Capa A de Cajas en Facturación
/// (12-cajas.md §9); reutilizable por cualquier módulo (p. ej. el
/// <c>leer-propias</c> de Almacén).
///
/// <para>
/// La implementación vive en <c>Api/Auth</c> y resuelve con el mismo dual-path
/// que <c>PermissionAuthorizationHandler</c>: service principal → claims
/// <c>permission</c> del token; humano → <c>IPermissionCache</c> +
/// <c>IPermissionLoader</c> (TTL 5 min, ADR-0007). Sin principal autenticado
/// o sin empresa seleccionada retorna <c>false</c>.
/// </para>
/// </summary>
public interface ICurrentUserPermissions
{
    /// <summary>True si el principal actual tiene el permiso canónico indicado.</summary>
    ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default);
}
