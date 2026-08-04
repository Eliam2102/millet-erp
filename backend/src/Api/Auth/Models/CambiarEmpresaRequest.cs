namespace Millet.Api.Auth.Models;

/// <summary>
/// Body de <c>POST /api/auth/cambiar-empresa</c>. El usuario autenticado
/// solicita re-emitir su JWT con un <c>current_empresa_id</c> distinto. La
/// empresa solicitada DEBE estar entre las que el usuario tiene asignación;
/// si no, el endpoint devuelve 403 (a diferencia del flujo de login que
/// hace fallback a la empresa anterior).
/// </summary>
/// <param name="EmpresaId">Empresa destino del nuevo JWT.</param>
public sealed record CambiarEmpresaRequest(Guid EmpresaId);
