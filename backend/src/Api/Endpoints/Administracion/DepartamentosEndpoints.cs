using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Administracion.Application.Departamentos;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Administracion;

/// <summary>
/// Endpoints HTTP del CRUD de Departamentos (F-Admin-PR2.3).
///
/// <list type="bullet">
///   <item><c>POST   /api/v1/admin/departamentos</c> — alta.</item>
///   <item><c>PATCH  /api/v1/admin/departamentos/{id}</c> — patch parcial.</item>
/// </list>
///
/// <para>Permiso: <c>admin.departamentos.gestionar</c>. Lectura sigue
/// pasando por <c>GET /api/v1/catalogos/departamentos</c> (B.1).</para>
/// </summary>
public static class DepartamentosEndpoints
{
    public static IEndpointRouteBuilder MapDepartamentosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/admin/departamentos")
            .WithTags("Administracion")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminDepartamentosGestionar);

        group.MapPost("/", async (
            [FromBody] CrearDepartamentoCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/admin/departamentos/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("CrearDepartamento")
        .WithSummary("Crear departamento")
        .Produces<DepartamentoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] ActualizarDepartamentoPayload payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ActualizarDepartamentoCommand(id, payload.Nombre), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ActualizarDepartamento")
        .WithSummary("PATCH parcial sobre departamento")
        .Produces<DepartamentoResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record ActualizarDepartamentoPayload(string? Nombre);
}
