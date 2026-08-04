using Millet.Identidad.Application.Ports;

namespace Millet.Identidad.Infrastructure.Stubs;

/// <summary>
/// Stub NoOp del <see cref="IEntraIdResolverPort"/> (F-Admin-PR4.1).
/// Siempre retorna <c>null</c>, dejando que el handler de
/// <c>CrearUsuarioCommand</c> caiga al placeholder <c>dev-{email}</c>
/// (ADR-0015). Es el wiring por defecto en dev/QA/Prod hasta que el
/// adapter real al Microsoft Graph API esté listo.
/// </summary>
public sealed class LocalEntraIdResolverNoOp : IEntraIdResolverPort
{
    // PLATFORM-TODO(<EntraIdResolver>): wireup real al Microsoft Graph API
    // post-MVP. Hoy retorna null para que CrearUsuario use el ObjectId/
    // Nombre proporcionados explícitamente o un placeholder dev-{email}.
    public Task<EntraIdResolution?> ResolverPorEmailAsync(string email, CancellationToken ct)
        => Task.FromResult<EntraIdResolution?>(null);
}
