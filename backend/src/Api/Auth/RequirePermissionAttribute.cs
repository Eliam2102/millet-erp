using Microsoft.AspNetCore.Authorization;

namespace Millet.Api.Auth;

/// <summary>
/// Atributo que exige un permiso específico para acceder al endpoint o acción.
/// Es sugar sintáctico sobre <c>[Authorize(Policy = "permiso:{code}")]</c>;
/// la policy la construye dinámicamente <see cref="PermissionPolicyProvider"/>.
///
/// <example>
/// <code>
/// [RequirePermission(PermisosCanonicos.IdentidadUsuariosCrear)]
/// public async Task&lt;IActionResult&gt; CrearUsuario(...) { ... }
/// </code>
/// </example>
///
/// <para>
/// Para Minimal API:
/// <code>
/// app.MapGet("/health", ...).RequireAuthorization("permiso:infra.health.leer");
/// </code>
/// (Minimal API consume nombres de policy directamente; este atributo es
/// solo para controllers).
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(string code) : base(PermissionPolicyProvider.Prefix + code)
    {
    }
}
