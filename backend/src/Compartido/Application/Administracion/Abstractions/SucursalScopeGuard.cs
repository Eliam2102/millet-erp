using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Abstractions;

/// <summary>
/// Guard de autorización por pertenencia a sucursal (F1-ADM-01 Fase 2,
/// sección C del plan). Complementa el <c>RequireAuthorization</c>
/// declarativo de los endpoints (que sólo valida el permiso de rol) con
/// una regla de negocio: al pedir departamentos/puestos/usuarios de una
/// <c>SucursalId</c>, el usuario autenticado debe estar asociado a esa
/// sucursal (vía <c>UsuarioSucursal</c>) O tener el permiso admin del
/// recurso — si no cumple ninguna, 403 <c>SUCURSAL_NO_ASOCIADA</c>.
///
/// <para>
/// Lanza <see cref="ForbiddenException"/>, que el
/// <c>GlobalExceptionHandler</c> ya mapea a HTTP 403 (mismo mecanismo que
/// usa <c>CrossTenantViolationException</c>) — a diferencia del
/// <c>RequireAuthorization</c> declarativo (política ASP.NET), este es
/// un chequeo de negocio dentro del handler, porque depende de datos
/// (la fila <c>UsuarioSucursal</c>), no sólo del rol.
/// </para>
///
/// <para>
/// Desacoplado de dónde vive la fuente de "está asociado" vía el delegado
/// <paramref name="estaAsociadoAsync"/>: los handlers de Compartido lo
/// resuelven con <see cref="IUsuarioSucursalReadPort"/> (implementado en
/// Identidad); el handler de Identidad (<c>ListarUsuariosPorSucursalQuery</c>)
/// puede resolverlo con una consulta directa a su propio DbContext sin
/// pasar por el puerto.
/// </para>
/// </summary>
/// <summary>
/// Códigos de permiso "admin" que el <see cref="SucursalScopeGuard"/>
/// acepta como bypass de la pertenencia a sucursal, por recurso. Se
/// duplican aquí como literales (en vez de referenciar
/// <c>Millet.Identidad.Domain.PermisosCanonicos</c>) porque Compartido
/// NO referencia el proyecto Identidad — evita el ciclo de dependencias
/// (Identidad ya referencia Compartido). Deben mantenerse en sync con
/// <c>PermisosCanonicos</c>; los tests de integración cruzan ambos.
/// </summary>
public static class SucursalScopeGuardPermisos
{
    public const string DepartamentosGestionar = "admin.sucursales.departamentos-gestionar";
    public const string PuestosGestionar = "admin.sucursales.puestos-gestionar";
    public const string UsuariosGestionar = "admin.sucursales.usuarios-gestionar";
}

public static class SucursalScopeGuard
{
    public static async Task VerificarAsync(
        Guid? currentUserId,
        string permisoAdmin,
        ICurrentUserPermissions permisos,
        Func<Guid, CancellationToken, Task<bool>> estaAsociadoAsync,
        CancellationToken cancellationToken)
    {
        if (await permisos.TieneAsync(permisoAdmin, cancellationToken))
        {
            return;
        }

        if (currentUserId is Guid userId
            && await estaAsociadoAsync(userId, cancellationToken))
        {
            return;
        }

        throw new ForbiddenException(
            "SUCURSAL_NO_ASOCIADA",
            "No tienes acceso a los datos de esta sucursal.");
    }
}
