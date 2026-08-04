using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Millet.Api.Auth;

/// <summary>
/// <see cref="IAuthorizationPolicyProvider"/> que construye policies
/// dinámicamente para cualquier nombre con prefijo <c>permiso:</c>. Evita
/// pre-registrar cientos de policies (una por código de permiso) en
/// <c>AddAuthorization()</c>.
///
/// <para>
/// Resolución:
/// <list type="bullet">
///   <item><c>permiso:fiscal.cfdi.timbrar</c> → policy con un <see cref="PermissionRequirement"/>("fiscal.cfdi.timbrar")</item>
///   <item>Cualquier otro nombre → fallback a <see cref="DefaultAuthorizationPolicyProvider"/></item>
/// </list>
/// </para>
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    public const string Prefix = "permiso:";

    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var code = policyName[Prefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(code))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}
