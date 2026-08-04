namespace Millet.Api.Auth.Models;

/// <summary>
/// Body de <c>POST /api/auth/sesion</c>. <see cref="EntraToken"/> es el
/// access token emitido por Entra que MSAL del frontend obtuvo. Si el
/// usuario quiere arrancar sesión en una empresa específica, la indica en
/// <see cref="EmpresaId"/>; si no, el orchestrator aplica selección
/// automática (última usada → única → ninguna).
/// </summary>
/// <param name="EntraToken">Access token de Entra ID (JWT).</param>
/// <param name="EmpresaId">Empresa específica solicitada (opcional).</param>
public sealed record LoginRequest(string EntraToken, Guid? EmpresaId);
