using Microsoft.AspNetCore.Http;
using Millet.SharedKernel.Application;

namespace Millet.SharedKernel.Infrastructure;

/// <summary>
/// Implementación de <see cref="ICurrentServicePrincipal"/> que lee del
/// <c>HttpContext.User</c> populado por el authentication scheme
/// <c>EntraServicePrincipal</c>. Filtra por el claim
/// <see cref="MilletClaimTypes.AuthType"/> = <see cref="MilletClaimTypes.AuthTypes.ServicePrincipal"/>;
/// si no matchea, todos los properties retornan default (null / vacío).
/// </summary>
public sealed class CurrentServicePrincipal : ICurrentServicePrincipal
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentServicePrincipal(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsServicePrincipal
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true) return false;
            var authType = user.FindFirst(MilletClaimTypes.AuthType)?.Value;
            return string.Equals(authType, MilletClaimTypes.AuthTypes.ServicePrincipal, StringComparison.Ordinal);
        }
    }

    public Guid? Id
    {
        get
        {
            if (!IsServicePrincipal) return null;
            var sub = _httpContextAccessor.HttpContext?.User.FindFirst(MilletClaimTypes.Sub)?.Value;
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }

    public Guid? EmpresaId
    {
        get
        {
            if (!IsServicePrincipal) return null;
            var raw = _httpContextAccessor.HttpContext?.User.FindFirst(MilletClaimTypes.CurrentEmpresaId)?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public string? Nombre
    {
        get
        {
            if (!IsServicePrincipal) return null;
            return _httpContextAccessor.HttpContext?.User.FindFirst(MilletClaimTypes.Name)?.Value;
        }
    }

    public Guid? EntraAppId
    {
        get
        {
            if (!IsServicePrincipal) return null;
            var raw = _httpContextAccessor.HttpContext?.User.FindFirst(MilletClaimTypes.EntraAppId)?.Value;
            return Guid.TryParse(raw, out var appId) ? appId : null;
        }
    }

    public IReadOnlyList<string> Permisos
    {
        get
        {
            if (!IsServicePrincipal) return Array.Empty<string>();
            var user = _httpContextAccessor.HttpContext?.User;
            if (user is null) return Array.Empty<string>();
            return user.FindAll(MilletClaimTypes.Permission)
                .Select(c => c.Value)
                .ToList();
        }
    }
}
