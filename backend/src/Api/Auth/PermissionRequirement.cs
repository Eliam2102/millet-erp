using Microsoft.AspNetCore.Authorization;

namespace Millet.Api.Auth;

/// <summary>
/// Requisito de autorización: el usuario debe tener el permiso identificado
/// por <see cref="Code"/> (formato <c>modulo.recurso.accion</c>) en la
/// empresa actualmente seleccionada (claim <c>current_empresa_id</c>).
///
/// Lo evalúa <see cref="PermissionAuthorizationHandler"/>; se construye
/// dinámicamente desde el nombre de policy <c>permiso:{Code}</c> vía
/// <see cref="PermissionPolicyProvider"/>.
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Code { get; }

    public PermissionRequirement(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Code no puede ser vacío.", nameof(code));
        }

        Code = code;
    }
}
