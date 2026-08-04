using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Application.Reorden;
using Millet.Almacen.Domain.Catalogo;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Catalogos.Domain;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Almacen.Reorden;

/// <summary>
/// Endpoints CRUD de la configuración de reorden N1/N2 (ADR-0047 PR5.A, enmienda
/// 2026-07-06). Bajo <c>/api/v1/almacen/reorden</c>. Mutaciones con
/// Idempotency-Key (ADR-0020) y RBAC granular (par leer/administrar). El motor
/// que consume estas configuraciones llega en 5.D.
/// </summary>
public static class AlmacenReordenEndpoints
{
    private const int LimitMax = 500;

    public static IEndpointRouteBuilder MapAlmacenReordenEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app
            .MapGroup("/api/v1/almacen/reorden")
            .WithTags("Almacen")
            .RequireAuthorization();

        // --- Listar ---
        g.MapGet("/", async (
            [FromQuery] Guid? articuloId,
            [FromQuery] NivelReorden? nivel,
            [FromQuery] Guid? entidadId,
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var (off, lim) = NormalizePaging(offset, limit);
            var response = await mediator.Send(
                new ListarConfiguracionesReordenQuery(articuloId, nivel, entidadId, estatus, off, lim), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenReordenRead)
        .WithName("ListarConfiguracionesReorden")
        .WithSummary("Listar configuraciones de reorden N1/N2 (ADR-0047 PR5.A)")
        .Produces<AlmacenPagedResponse<ConfiguracionReordenListItem>>(StatusCodes.Status200OK);

        // --- Obtener por Id ---
        g.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var detalle = await mediator.Send(new ObtenerConfiguracionReordenPorIdQuery(id), ct);
            return detalle is null ? Results.NotFound() : Results.Ok(detalle);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenReordenRead)
        .WithName("ObtenerConfiguracionReordenPorId")
        .WithSummary("Obtener configuración de reorden por id")
        .Produces<ConfiguracionReordenListItem>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- Crear (o reactivar) ---
        g.MapPost("/", async (
            CrearConfiguracionReordenCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/almacen/reorden/{response.Id}", response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenReordenAdministrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("CrearConfiguracionReorden")
        .WithSummary("Crear configuración de reorden N1/N2 (valida entidad, asignación y exclusión N1⊕N2)")
        .Produces<ConfiguracionReordenResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- Editar política (llave inmutable) ---
        g.MapPatch("/{id:guid}", async (
            Guid id,
            EditarConfiguracionReordenCommand body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (id != body.Id) return Results.BadRequest(new { error = "ID en URL no coincide con el body" });
            var response = await mediator.Send(body, ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenReordenAdministrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("EditarConfiguracionReorden")
        .WithSummary("Editar política de reorden (min/máx/reorden/bandera/objetivo)")
        .Produces<ConfiguracionReordenResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- Desactivar (idempotente) ---
        g.MapPost("/{id:guid}/desactivar", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new DesactivarConfiguracionReordenCommand(id), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenReordenAdministrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("DesactivarConfiguracionReorden")
        .WithSummary("Desactivar configuración de reorden")
        .Produces<ConfiguracionReordenResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static (int off, int lim) NormalizePaging(int? offset, int? limit)
    {
        var off = offset is < 0 ? 0 : offset ?? 0;
        var lim = limit is null or <= 0 ? 50 : Math.Min(limit.Value, LimitMax);
        return (off, lim);
    }
}
