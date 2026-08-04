using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Millet.Api.Auth;

/// <summary>
/// Convierte el 403 del middleware de autorización en Problem Details
/// (RFC 7807, ADR-0010) nombrando el permiso canónico faltante.
///
/// <para>Sin este handler, un fallo de policy <c>permiso:*</c> respondía
/// 403 con body vacío: el middleware de autorización no pasa por
/// <see cref="Millet.Api.Web.GlobalExceptionHandler"/> (que solo traduce
/// excepciones de los handlers, p. ej. <c>ForbiddenException</c>). El
/// frontend caía a su fallback y mostraba un toast críptico "HTTP 403",
/// sin pista de qué permiso pedirle al administrador.</para>
///
/// <para>El 401 (challenge) y el flujo exitoso delegan al handler default
/// de ASP.NET; el override de challenge del service principal
/// (<see cref="EntraServicePrincipalAuthenticationHandler"/>) ocurre en
/// autenticación, antes de este punto, y no se ve afectado.</para>
/// </summary>
public sealed class PermisoFaltanteResultHandler : IAuthorizationMiddlewareResultHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Forbidden || context.Response.HasStarted)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var permisosFaltantes = (authorizeResult.AuthorizationFailure?.FailedRequirements
                ?? Enumerable.Empty<IAuthorizationRequirement>())
            .OfType<PermissionRequirement>()
            .Select(r => r.Code)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        var code = permisosFaltantes.Length > 0 ? "PERMISO_FALTANTE" : "ACCESO_DENEGADO";
        var detail = permisosFaltantes.Length > 0
            ? $"Requiere el permiso '{string.Join("', '", permisosFaltantes)}'. " +
              "Pide al administrador que lo agregue a tu rol en la empresa seleccionada."
            : "No tienes acceso a este recurso en la empresa seleccionada.";

        var problem = new ProblemDetails
        {
            Type = $"https://millet-erp/errors/{code.ToLowerInvariant()}",
            Title = "Sin permiso para esta operación.",
            Status = StatusCodes.Status403Forbidden,
            Detail = detail,
            Instance = context.Request.Path,
        };
        problem.Extensions["traceId"] = traceId;
        problem.Extensions["code"] = code;
        if (permisosFaltantes.Length > 0)
        {
            problem.Extensions["permisosFaltantes"] = permisosFaltantes;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(
            context.Response.Body, problem, JsonOptions, context.RequestAborted);
    }
}
