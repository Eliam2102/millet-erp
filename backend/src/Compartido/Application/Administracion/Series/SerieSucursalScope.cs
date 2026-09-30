using Millet.Administracion.Application.Abstractions;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Series;

public sealed class SerieSucursalScope
{
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentUserPermissions _permisos;
    private readonly IUsuarioSucursalReadPort _usuarioSucursales;

    public SerieSucursalScope(
        ICurrentUserContext currentUser,
        ICurrentUserPermissions permisos,
        IUsuarioSucursalReadPort usuarioSucursales)
    {
        _currentUser = currentUser;
        _permisos = permisos;
        _usuarioSucursales = usuarioSucursales;
    }

    public async Task<bool> PuedeGestionarGlobalesAsync(CancellationToken ct) =>
        await _permisos.TieneAsync(SucursalScopeGuardPermisos.SeriesGlobalesGestionar, ct);

    public async Task<IReadOnlyList<Guid>> ListarAutorizadasAsync(CancellationToken ct)
    {
        if (_currentUser.UserId is not Guid userId) return [];
        return await _usuarioSucursales.ListarIdsAsync(userId, ct);
    }

    public async Task VerificarAsync(Guid? sucursalId, CancellationToken ct)
    {
        if (await PuedeGestionarGlobalesAsync(ct)) return;

        if (sucursalId is not Guid sid)
        {
            throw new ForbiddenException(
                "SERIE_GLOBAL_RESTRINGIDA",
                "Sólo un administrador corporativo puede gestionar series globales.");
        }

        await SucursalScopeGuard.VerificarAsync(
            _currentUser.UserId,
            SucursalScopeGuardPermisos.SeriesGlobalesGestionar,
            _permisos,
            (userId, token) => _usuarioSucursales.EstaAsociadoAsync(userId, sid, token),
            ct);
    }
}
