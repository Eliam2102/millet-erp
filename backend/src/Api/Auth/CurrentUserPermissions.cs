using Millet.Identidad.Application;
using Millet.SharedKernel.Application;

namespace Millet.Api.Auth;

/// <summary>
/// Implementación de <see cref="ICurrentUserPermissions"/> sobre el request
/// actual. Replica el dual-path de <see cref="PermissionAuthorizationHandler"/>
/// sin pasar por el pipeline de authorization (los handlers la consultan para
/// scoping de datos, no para 403):
///
/// <list type="number">
///   <item><b>Service principal:</b> el claim <see cref="MilletClaimTypes.AuthType"/>
///         vale <c>service_principal</c> → los permisos ya vienen como claims
///         <c>permission</c>×N en el principal (frescos por request).</item>
///   <item><b>Humano:</b> <see cref="IPermissionCache"/> primero; en miss carga
///         vía <see cref="IPermissionLoader"/> y cachea (TTL 5 min, ADR-0007) —
///         misma entrada de cache que el authorization handler, así una
///         invalidación cubre ambos consumidores.</item>
/// </list>
///
/// Sin usuario autenticado o sin <c>current_empresa_id</c> responde
/// <c>false</c> (el bearer middleware ya rechazó; esto es defensa en handlers
/// que corran fuera de un request, p. ej. workers).
/// </summary>
public sealed class CurrentUserPermissions : ICurrentUserPermissions
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICurrentUserContext _user;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly IPermissionCache _cache;
    private readonly IPermissionLoader _loader;

    public CurrentUserPermissions(
        IHttpContextAccessor httpContextAccessor,
        ICurrentUserContext user,
        ICurrentEmpresaContext empresa,
        IPermissionCache cache,
        IPermissionLoader loader)
    {
        _httpContextAccessor = httpContextAccessor;
        _user = user;
        _empresa = empresa;
        _cache = cache;
        _loader = loader;
    }

    public async ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permiso);

        if (_user.UserId is not Guid userId || _empresa.Current is not Guid empresaId)
        {
            return false;
        }

        var principal = _httpContextAccessor.HttpContext?.User;
        var authType = principal?.FindFirst(MilletClaimTypes.AuthType)?.Value;
        if (string.Equals(authType, MilletClaimTypes.AuthTypes.ServicePrincipal, StringComparison.Ordinal))
        {
            return principal!.FindAll(MilletClaimTypes.Permission)
                .Any(c => string.Equals(c.Value, permiso, StringComparison.Ordinal));
        }

        var permisos = await _cache.GetAsync(userId, empresaId, cancellationToken);
        if (permisos is null)
        {
            permisos = await _loader.LoadForUserInEmpresaAsync(userId, empresaId, cancellationToken);
            await _cache.SetAsync(userId, empresaId, permisos, cancellationToken);
        }

        return permisos.Contains(permiso);
    }
}
