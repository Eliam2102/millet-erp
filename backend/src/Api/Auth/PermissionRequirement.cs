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

    /// <summary>Códigos aceptados: <c>a|b</c> pasa con cualquiera de los dos (catálogos que comparten varias pantallas).</summary>
    public IReadOnlyList<string> Codes { get; }

    public PermissionRequirement(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Code no puede ser vacío.", nameof(code));
        }

        Code = code;
        Codes = code.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
