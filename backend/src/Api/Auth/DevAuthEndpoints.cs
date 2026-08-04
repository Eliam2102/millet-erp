#if DEBUG
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth.Models;
using Millet.SharedKernel.Application;

namespace Millet.Api.Auth;

/// <summary>
/// Endpoints solo para desarrollo local (compilación condicional
/// <c>#if DEBUG</c>, ver ADR-0015). El binario de Release ni siquiera
/// contiene este código — múltiples capas de seguridad para que el modo
/// fake jamás llegue a un ambiente real:
/// <list type="number">
///   <item><c>#if DEBUG</c> — no compilado en Release</item>
///   <item><c>AuthModeValidator</c> en arranque rechaza FakeForLocalDev fuera de Development</item>
///   <item>Re-validación de <c>Auth:Mode</c> en cada request (defense in depth)</item>
/// </list>
/// </summary>
public static class DevAuthEndpoints
{
    public static IEndpointRouteBuilder MapDevAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/dev/fake-login", async (
            [FromBody] FakeLoginRequest request,
            LoginOrchestrator orchestrator,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            // Defense in depth: aunque el AuthModeValidator ya falló al
            // arranque si FakeForLocalDev en non-Development, verificamos
            // de nuevo aquí. Si alguien forzó este endpoint en un build
            // accidental, devolvemos 404 para no revelar su existencia.
            var mode = configuration.GetValue<AuthMode?>("Auth:Mode") ?? AuthMode.EntraId;
            if (mode != AuthMode.FakeForLocalDev)
            {
                return Results.NotFound();
            }

            var response = await orchestrator.LoginWithFakeOidAsync(
                request.EntraOid,
                request.Email,
                request.Nombre,
                request.EmpresaId,
                cancellationToken);

            return Results.Ok(response);
        })
        .AllowAnonymous()
        .WithName("PostFakeLogin")
        .WithTags("DevAuth");

        return app;
    }
}
#endif
