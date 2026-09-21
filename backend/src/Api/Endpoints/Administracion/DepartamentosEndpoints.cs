using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Administracion.Application.Departamentos;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Catalogos.Domain;
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
                new ListarDepartamentosQuery(offset ?? 0, limit ?? 50, q, estatus), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminDepartamentosLeer)
        .WithName("ListarDepartamentosAdmin")
        .WithSummary("Listar departamentos administrativos")
        .Produces<ListarDepartamentosResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/", async (
            [FromBody] CrearDepartamentoCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/admin/departamentos/{response.Id}", response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminDepartamentosGestionar)
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
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminDepartamentosGestionar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ActualizarDepartamento")
        .WithSummary("PATCH parcial sobre departamento")
        .Produces<DepartamentoResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/desactivar", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new DesactivarDepartamentoCommand(id), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminDepartamentosGestionar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("DesactivarDepartamento")
        .WithSummary("Desactivar departamento (baja lógica, idempotente)")
        .Produces<DepartamentoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/reactivar", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new ReactivarDepartamentoCommand(id), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminDepartamentosGestionar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ReactivarDepartamento")
        .WithSummary("Reactivar departamento")
        .Produces<DepartamentoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record ActualizarDepartamentoPayload(string? Nombre);
}
