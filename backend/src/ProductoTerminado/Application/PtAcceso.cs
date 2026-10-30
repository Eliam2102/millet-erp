using Millet.Administracion.Application.Abstractions;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.Application;

/// <summary>Guardas de permiso y sucursal (ADR-0051) compartidas por los casos de uso de Producto Terminado.</summary>
public sealed class PtAcceso(ICurrentUserContext user, ICurrentUserPermissions permisos, IUsuarioSucursalReadPort sucursales)
{
    public Guid Actor => user.UserId ?? throw new UnauthorizedAccessException();

    public async Task ExigirPermisoAsync(string permiso, CancellationToken ct)
    {
        if (!await permisos.TieneAsync(permiso, ct))
            throw new ForbiddenException("PT_PERMISO_REQUERIDO", "No tiene permiso para esta acción.");
    }

    /// <summary>Sucursales visibles para el usuario; null = todas (tiene el permiso de bypass).</summary>
    public async Task<IReadOnlyList<Guid>?> SucursalesVisiblesAsync(string permisoTodas, CancellationToken ct)
    {
        if (await permisos.TieneAsync(permisoTodas, ct)) return null;
        return await sucursales.ListarIdsAsync(Actor, ct);
    }

    /// <summary>Un registro sin sucursal resuelta solo es accionable con el bypass corporativo.</summary>
    public async Task VerificarSucursalAsync(Guid? sucursalId, string permisoTodas, CancellationToken ct)
    {
        if (await permisos.TieneAsync(permisoTodas, ct)) return;
        if (sucursalId is null)
            throw new ForbiddenException("SUCURSAL_NO_DETERMINADA", "No se pudo determinar la sucursal del registro.");
        await SucursalScopeGuard.VerificarAsync(user.UserId, permisoTodas, permisos,
            (uid, c) => sucursales.EstaAsociadoAsync(uid, sucursalId.Value, c), ct);
    }
}
