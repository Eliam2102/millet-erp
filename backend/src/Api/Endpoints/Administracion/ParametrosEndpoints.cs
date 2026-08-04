using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Administracion.Application.Parametros;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Administracion;

/// <summary>
/// Endpoints HTTP para parámetros globales del sistema (F-Admin-PR7.1).
///
/// <list type="bullet">
///   <item><c>GET /api/v1/admin/parametros[?modulo=]</c> — lista (permiso
///         <c>admin.parametros.leer</c>). <c>modulo=""</c> filtra
///         solo a los globales (sin módulo).</item>
///   <item><c>PATCH /api/v1/admin/parametros/{clave}</c> — actualiza el
///         valor; el dominio valida contra <c>Tipo</c> (permiso
///         <c>admin.parametros.editar</c>; Idempotency-Key requerido).</item>
/// </list>
/// </summary>
public static class ParametrosEndpoints
{
    public static IEndpointRouteBuilder MapParametrosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/admin/parametros")
            .WithTags("Administracion");

        group.MapGet("/", async (
            [FromQuery] string? modulo,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new ListarParametrosQuery(modulo), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminParametrosLeer)
        .WithName("GetParametros")
        .WithSummary("Listar parámetros globales (filtro opcional por módulo)")
        .Produces<ListarParametrosResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPatch("/{clave}", async (
            string clave,
            [FromBody] ActualizarParametroPayload body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ActualizarParametroCommand(clave, body.Valor),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminParametrosEditar)
        .WithName("PatchParametro")
        .WithSummary("Actualizar el valor de un parámetro global por clave")
        .Produces<ParametroResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>Body del PATCH: solo el valor (Clave llega por URL).</summary>
    public sealed record ActualizarParametroPayload(string Valor);
}
