using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Application.Roles;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Identidad;

/// <summary>
/// Endpoints HTTP del CRUD de Roles + matriz de permisos + asociación de
/// grupos de Microsoft Entra ID (F-Admin-PR3.2).
///
/// <list type="bullet">
///   <item><c>GET    /api/v1/identidad/roles</c> — paginado (permiso
///         <c>identidad.roles.leer</c>).</item>
///   <item><c>POST   /api/v1/identidad/roles</c> — alta (permiso
///         <c>identidad.roles.crear</c>, Idempotency-Key).</item>
///   <item><c>GET    /api/v1/identidad/roles/{id}</c> — detalle con
///         permisos asignados y grupos Entra ID.</item>
///   <item><c>PATCH  /api/v1/identidad/roles/{id}</c> — PATCH parcial.</item>
///   <item><c>DELETE /api/v1/identidad/roles/{id}</c> — soft-delete.</item>
///   <item><c>PUT    /api/v1/identidad/roles/{id}/permisos</c> — batch atómico.</item>
///   <item><c>POST   /api/v1/identidad/roles/{id}/grupos-entra-id</c> — asociar grupo.</item>
///   <item><c>DELETE /api/v1/identidad/roles/grupos-entra-id/{rolGrupoEntraIdId}</c>.</item>
/// </list>
/// </summary>
public static class RolesEndpoints
{
    public static IEndpointRouteBuilder MapRolesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/identidad/roles")
            .WithTags("Identidad");

        // --- LIST ---
        group.MapGet("/", async (
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            [FromQuery] bool? soloActivos,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ListarRolesQuery(
                    Offset: offset ?? 0,
                    Limit: limit ?? 50,
                    SoloActivos: soloActivos),
                ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IdentidadRolesLeer)
        .WithName("ListarRoles")
        .WithSummary("Listar roles paginados")
        .Produces<ListarRolesResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // --- CREATE ---
        group.MapPost("/", async (
            [FromBody] CrearRolCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/identidad/roles/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IdentidadRolesCrear)
        .WithName("CrearRol")
        .WithSummary("Crear rol nuevo")
        .Produces<RolResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // --- DETALLE ---
        group.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new ObtenerRolQuery(id), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IdentidadRolesLeer)
        .WithName("ObtenerRol")
        .WithSummary("Detalle de rol con permisos y grupos Entra ID")
        .Produces<RolDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- PATCH ---
        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] ActualizarRolPayload payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var command = new ActualizarRolCommand(
                id,
                payload.Nombre,
                payload.Descripcion,
                payload.LimpiarDescripcion ?? false);
            var response = await mediator.Send(command, ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IdentidadRolesEditar)
        .WithName("ActualizarRol")
        .WithSummary("PATCH parcial sobre rol")
        .Produces<RolResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- DELETE (soft-delete) ---
        group.MapDelete("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await mediator.Send(new EliminarRolCommand(id), ct);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IdentidadRolesEliminar)
        .WithName("EliminarRol")
        .WithSummary("Soft-delete de rol (Activo=false)")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- ASIGNAR PERMISOS (batch atómico) ---
        group.MapPut("/{id:guid}/permisos", async (
            Guid id,
            [FromBody] AsignarPermisosPayload payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new AsignarPermisosARolCommand(id, payload.PermisoIds ?? Array.Empty<Guid>()),
                ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IdentidadRolesAsignarPermisos)
        .WithName("AsignarPermisosARol")
        .WithSummary("Reemplaza la matriz de permisos del rol (batch atómico)")
        .Produces<RolResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- ASOCIAR GRUPO ENTRA ID ---
        group.MapPost("/{id:guid}/grupos-entra-id", async (
            Guid id,
            [FromBody] AsociarGrupoEntraIdPayload payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new AsociarGrupoEntraIdARolCommand(id, payload.ObjectId, payload.Nombre),
                ct);
            return Results.Created(
                $"/api/v1/identidad/roles/grupos-entra-id/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IdentidadRolesGruposEntraIdGestionar)
        .WithName("AsociarGrupoEntraIdARol")
        .WithSummary("Asocia un grupo Microsoft Entra ID al rol")
        .Produces<RolGrupoEntraIdResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // --- DESASOCIAR GRUPO ENTRA ID (sub-ruta plana por id de la asociación) ---
        group.MapDelete("/grupos-entra-id/{rolGrupoEntraIdId:guid}", async (
            Guid rolGrupoEntraIdId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await mediator.Send(new DesasociarGrupoEntraIdDeRolCommand(rolGrupoEntraIdId), ct);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IdentidadRolesGruposEntraIdGestionar)
        .WithName("DesasociarGrupoEntraIdDeRol")
        .WithSummary("Borra la asociación rol↔grupo Entra ID")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>Payload del PATCH /roles/{id}.</summary>
    public sealed record ActualizarRolPayload(
        string? Nombre,
        string? Descripcion,
        bool? LimpiarDescripcion);

    /// <summary>Payload del PUT /roles/{id}/permisos.</summary>
    public sealed record AsignarPermisosPayload(IReadOnlyList<Guid>? PermisoIds);

    /// <summary>Payload del POST /roles/{id}/grupos-entra-id.</summary>
    public sealed record AsociarGrupoEntraIdPayload(string ObjectId, string Nombre);
}
