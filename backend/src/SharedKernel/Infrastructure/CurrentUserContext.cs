using Microsoft.AspNetCore.Http;
using Millet.SharedKernel.Application;

namespace Millet.SharedKernel.Infrastructure;

/// <summary>
/// Implementación de <see cref="ICurrentUserContext"/> que lee la identidad
/// del usuario desde los claims del JWT validado por el bearer middleware.
/// Si no hay <c>HttpContext</c> activo (jobs en background, migrations,
/// arranque) o el request no está autenticado, retorna <c>null</c>. Los
/// interceptors EF Core toleran ese caso y los campos de auditoría caen al
/// string <c>"system"</c>.
/// </summary>
public sealed class CurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var sub = GetClaim(MilletClaimTypes.Sub);
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }

    public string? UserName => GetClaim(MilletClaimTypes.Name);

    private string? GetClaim(string type)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return user.FindFirst(type)?.Value;
    }
}
