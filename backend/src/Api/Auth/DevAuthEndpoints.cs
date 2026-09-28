using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth.Models;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

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
            AuthAccessAuditWriter audit,
            HttpContext http,
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

            try
            {
                var response = await orchestrator.LoginWithFakeOidAsync(
                    request.EntraOid, request.Email, request.Nombre,
                    request.EmpresaId, cancellationToken);
                await audit.WriteAsync("acceso", response.Usuario.Id,
                    response.Empresas.FirstOrDefault(e => e.EsLaActual)?.Id,
                    "FakeForLocalDev", null, http.Connection.RemoteIpAddress, cancellationToken);
                return Results.Ok(response);
            }
            catch (ForbiddenException ex)
            {
                await audit.WriteAsync("acceso_denegado", null, null,
                    "FakeForLocalDev", ex.Code, http.Connection.RemoteIpAddress, cancellationToken);
                throw;
            }
        })
        .AllowAnonymous()
        .WithName("PostFakeLogin")
        .WithTags("DevAuth");

        return app;
    }
}
