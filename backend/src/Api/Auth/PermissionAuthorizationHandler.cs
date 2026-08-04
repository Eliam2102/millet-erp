using Microsoft.AspNetCore.Authorization;
using Millet.Identidad.Application;
using Millet.SharedKernel.Application;

namespace Millet.Api.Auth;

/// <summary>
/// Evalúa un <see cref="PermissionRequirement"/> contra la sesión actual.
/// Maneja DOS fuentes de permisos sin XOR:
///
/// <list type="number">
///   <item><b>Service principal (PR A):</b> el token Entra ya trae los
///         permisos como claims <c>permission</c>×N (puestos por
///         <c>EntraServicePrincipalAuthenticationHandler</c>). Se leen
///         directo del <c>ClaimsPrincipal</c> sin tocar BD.</item>
///   <item><b>Humano (flujo clásico):</b> consulta
///         <see cref="IPermissionCache"/> primero; en miss carga vía
///         <see cref="IPermissionLoader"/> y cachea (TTL 5 min,
///         ADR-0007).</item>
/// </list>
///
/// <para>
/// El detector es el claim <see cref="MilletClaimTypes.AuthType"/>: si
/// vale <see cref="MilletClaimTypes.AuthTypes.ServicePrincipal"/>, vamos
/// por la rama de claims; cualquier otro valor (incluyendo ausencia) cae
/// al flujo humano. Esto preserva 100% el path existente para humanos
/// (regresión chequeada en tests integration).
/// </para>
///
/// <para>
/// Falla silenciosamente (no llama <c>context.Succeed</c>) cuando:
/// <list type="bullet">
///   <item>El usuario no está autenticado (<c>UserId == null</c>) — el bearer middleware ya rechazó pero por defensa.</item>
///   <item>No hay <c>current_empresa_id</c> en el JWT.</item>
///   <item>El permiso requerido no está en el set efectivo.</item>
/// </list>
/// El framework convierte el fail en HTTP 403.
/// </para>
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly ICurrentUserContext _user;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly IPermissionCache _cache;
    private readonly IPermissionLoader _loader;

    public PermissionAuthorizationHandler(
        ICurrentUserContext user,
        ICurrentEmpresaContext empresa,
        IPermissionCache cache,
        IPermissionLoader loader)
    {
        _user = user;
        _empresa = empresa;
        _cache = cache;
        _loader = loader;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (_user.UserId is not Guid userId)
        {
            return;
        }

        if (_empresa.Current is not Guid empresaId)
        {
            return;
        }

        var authType = context.User.FindFirst(MilletClaimTypes.AuthType)?.Value;
        if (string.Equals(authType, MilletClaimTypes.AuthTypes.ServicePrincipal, StringComparison.Ordinal))
        {
            // Caso SP: permisos como claims, set armado por el handler
            // de auth. NO consultamos BD ni cache (los permisos del SP
            // pueden cambiar en BD pero el handler los lee fresh por
            // request → ya considerados al construir el principal).
            var spHasPermission = context.User.FindAll(MilletClaimTypes.Permission)
                .Any(c => string.Equals(c.Value, requirement.Code, StringComparison.Ordinal));

            if (spHasPermission)
            {
                context.Succeed(requirement);
            }
            return;
        }

        // Caso humano (flujo clásico, sin cambios funcionales).
        var permisos = await _cache.GetAsync(userId, empresaId, CancellationToken.None);
        if (permisos is null)
        {
            permisos = await _loader.LoadForUserInEmpresaAsync(userId, empresaId, CancellationToken.None);
            await _cache.SetAsync(userId, empresaId, permisos, CancellationToken.None);
        }

        if (permisos.Contains(requirement.Code))
        {
            context.Succeed(requirement);
        }
    }
}
