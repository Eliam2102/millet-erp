using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Administracion.Application.Puestos;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Catalogos.Domain;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Administracion;

/// <summary>
/// Endpoints HTTP del CRUD de Puestos (ADM-PR1,
/// doc 10-catalogo-puestos-empleados).
///
/// <list type="bullet">
///   <item><c>POST   /api/v1/admin/puestos</c> — alta.</item>
///   <item><c>PATCH  /api/v1/admin/puestos/{id}</c> — patch parcial.</item>
///   <item><c>POST   /api/v1/admin/puestos/{id}/desactivar</c> — baja lógica.</item>
/// </list>
///
/// <para>Permiso: <c>admin.puestos.gestionar</c>. Lectura por
/// <c>GET /api/v1/catalogos/puestos</c>.</para>
/// </summary>
public static class PuestosEndpoints
{
    public static IEndpointRouteBuilder MapPuestosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/admin/puestos")
            .WithTags("Administracion");

        group.MapGet("/", async (
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            [FromQuery] string? q,
            [FromQuery] EstatusCatalogo? estatus,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ListarPuestosQuery(offset ?? 0, limit ?? 50, q, estatus), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminPuestosLeer)
        .WithName("ListarPuestosAdmin")
        .WithSummary("Listar puestos administrativos")
        .Produces<ListarPuestosResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/", async (
            [FromBody] CrearPuestoCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/admin/puestos/{response.Id}", response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminPuestosGestionar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("CrearPuesto")
        .WithSummary("Crear puesto")
        .Produces<PuestoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] ActualizarPuestoPayload payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ActualizarPuestoCommand(id, payload.Nombre), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminPuestosGestionar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ActualizarPuesto")
        .WithSummary("PATCH parcial sobre puesto")
        .Produces<PuestoResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/desactivar", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new DesactivarPuestoCommand(id), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminPuestosGestionar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("DesactivarPuesto")
        .WithSummary("Desactivar puesto (baja lógica, idempotente)")
        .Produces<PuestoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/reactivar", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new ReactivarPuestoCommand(id), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminPuestosGestionar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ReactivarPuesto")
        .WithSummary("Reactivar puesto (idempotente)")
        .Produces<PuestoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record ActualizarPuestoPayload(string? Nombre);
}
