using Millet.Administracion.Application.Abstractions;
using Millet.Administracion.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Series;

public sealed class SerieSucursalScope
{
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserPermissions _permisos;
    private readonly IUsuarioSucursalReadPort _usuarioSucursales;

    public SerieSucursalScope(
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserPermissions permisos,
        IUsuarioSucursalReadPort usuarioSucursales)
    {
        _currentUser = currentUser;
        _currentEmpresa = currentEmpresa;
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

    public void VerificarEmpresa(Guid empresaId)
    {
        // Procesos del sistema (workers en Bypass, sin JWT) mandan su empresa explícita: no hay usuario contra quien validar.
        if (_currentEmpresa.IsBypassed && _currentEmpresa.Current is null) return;
        var actual = _currentEmpresa.Current
            ?? throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        if (actual != empresaId)
            throw new CrossTenantViolationException(nameof(Serie), actual, empresaId);
    }
}
