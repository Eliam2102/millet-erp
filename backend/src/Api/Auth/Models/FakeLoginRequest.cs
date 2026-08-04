namespace Millet.Api.Auth.Models;

/// <summary>
/// Body de <c>POST /api/dev/fake-login</c> (compilación condicional
/// <c>#if DEBUG</c>, ver ADR-0015). Permite simular un login sin Entra real.
///
/// <para>
/// <see cref="EntraOid"/> identifica al usuario; si no existe en BD, el
/// orchestrator lo auto-provisiona usando <see cref="Email"/> y
/// <see cref="Nombre"/> como semilla. Para suite de usuarios seed (ver
/// ADR-0015): "dev-superadmin", "dev-facturador", etc.
/// </para>
/// </summary>
/// <param name="EntraOid">OID sintético del usuario fake.</param>
/// <param name="Email">Email del usuario (para auto-provisión si no existe).</param>
/// <param name="Nombre">Nombre del usuario (para auto-provisión).</param>
/// <param name="EmpresaId">Empresa específica solicitada (opcional).</param>
public sealed record FakeLoginRequest(
    string EntraOid,
    string Email,
    string Nombre,
    Guid? EmpresaId);
