using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Almacen.Application.Asignaciones;
using Millet.Almacen.Application.Catalogo;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Catalogos.Domain;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Almacen.Asignaciones;

/// <summary>
/// Endpoints CRUD de la asignación artículo→ubicación (OITW, ADR-0047 PR3).
/// Bajo <c>/api/v1/almacen/asignaciones</c>. Mutaciones con Idempotency-Key
/// (ADR-0020) y RBAC granular (par leer/administrar).
/// </summary>
public static class AlmacenAsignacionesEndpoints
{
    private const int LimitMax = 500;

    public static IEndpointRouteBuilder MapAlmacenAsignacionesEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app
            .MapGroup("/api/v1/almacen/asignaciones")
            .WithTags("Almacen")
            .RequireAuthorization();

        // --- Listar ---
        g.MapGet("/", async (
            [FromQuery] Guid? ubicacionId,
            [FromQuery] Guid? articuloId,
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var (off, lim) = NormalizePaging(offset, limit);
            var response = await mediator.Send(
                new ListarAsignacionesQuery(ubicacionId, articuloId, estatus, off, lim), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenAsignacionesRead)
        .WithName("ListarAsignaciones")
        .WithSummary("Listar asignaciones artículo→ubicación (ADR-0047 PR3)")
        .Produces<AlmacenPagedResponse<AsignacionListItem>>(StatusCodes.Status200OK);

        // --- Obtener por Id ---
        g.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var detalle = await mediator.Send(new ObtenerAsignacionPorIdQuery(id), ct);
            return detalle is null ? Results.NotFound() : Results.Ok(detalle);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenAsignacionesRead)
        .WithName("ObtenerAsignacionPorId")
        .WithSummary("Obtener asignación por id")
        .Produces<AsignacionListItem>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- Asignar (crea/reactiva + fila-en-0) ---
        g.MapPost("/", async (
            AsignarArticuloAUbicacionCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/almacen/asignaciones/{response.Id}", response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenAsignacionesAdministrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("AsignarArticuloAUbicacion")
        .WithSummary("Asignar artículo a ubicación (crea la fila de saldo en 0)")
        .Produces<AsignacionResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- Desasignar (guardrail EN_USO + borra fila-en-0) ---
        g.MapPost("/{id:guid}/desasignar", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new DesasignarArticuloDeUbicacionCommand(id), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenAsignacionesAdministrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("DesasignarArticuloDeUbicacion")
        .WithSummary("Desasignar artículo de ubicación (bloqueado si hay saldo)")
        .Produces<AsignacionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static (int off, int lim) NormalizePaging(int? offset, int? limit)
    {
        var off = offset is < 0 ? 0 : offset ?? 0;
        var lim = limit is null or <= 0 ? 50 : Math.Min(limit.Value, LimitMax);
        return (off, lim);
    }
}
