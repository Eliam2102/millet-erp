using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth.Models;
using Millet.SharedKernel.Application;

namespace Millet.Api.Auth;

/// <summary>
/// Endpoint solo para desarrollo local (ver ADR-0015). El endpoint se incluye
/// también en Release para que las pruebas de integración CI puedan ejercitar
/// el flujo completo; múltiples capas de seguridad impiden usarlo fuera del
/// modo fake local:
/// <list type="number">
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
            IHostEnvironment environment,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            // Defense in depth: aunque el AuthModeValidator ya falló al
            // arranque si FakeForLocalDev en non-Development, verificamos
            // de nuevo aquí. Si alguien forzó este endpoint en un build
            // accidental, devolvemos 404 para no revelar su existencia.
            if (!environment.IsDevelopment() ||
                configuration.GetValue<AuthMode?>("Auth:Mode") != AuthMode.FakeForLocalDev)
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
