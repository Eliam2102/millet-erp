using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Domain.Catalogo;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Catalogos.Domain;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Almacen.Catalogo;

/// <summary>
/// Endpoints CRUD del catálogo Almacén / SubAlmacén (F1-PR1).
/// Bajo <c>/api/v1/almacen/almacenes</c> y <c>/api/v1/almacen/sub-almacenes</c>.
/// </summary>
public static class AlmacenCatalogoEndpoints
{
    private const int LimitMax = 500;

    public static IEndpointRouteBuilder MapAlmacenCatalogoEndpoints(this IEndpointRouteBuilder app)
    {
        var almacenes = app
            .MapGroup("/api/v1/almacen/almacenes")
            .WithTags("Almacen")
            .RequireAuthorization();

        // --- Listar Almacenes ---
        almacenes.MapGet("/", async (
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] Guid? sucursalId,
            [FromQuery] string? q,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var (off, lim) = NormalizePaging(offset, limit);
            var response = await mediator.Send(
                new ListarAlmacenesQuery(estatus, sucursalId, q, off, lim), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenAlmacenesRead)
        .WithName("ListarAlmacenesAlmacen")
        .WithSummary("Listar almacenes del módulo Almacén (F1-PR1)")
        .Produces<AlmacenPagedResponse<AlmacenListItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // --- Obtener Almacén por Id ---
        almacenes.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var detalle = await mediator.Send(new ObtenerAlmacenPorIdQuery(id), ct);
            return detalle is null ? Results.NotFound() : Results.Ok(detalle);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenAlmacenesRead)
        .WithName("ObtenerAlmacenPorId")
        .WithSummary("Obtener almacén con sub-almacenes")
        .Produces<AlmacenDetalle>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- Crear Almacén ---
        almacenes.MapPost("/", async (
            CrearAlmacenCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/almacen/almacenes/{response.Id}", response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenAlmacenesAdministrar)
        .WithName("CrearAlmacen")
        .WithSummary("Crear almacén (F1-PR1)")
        .Produces<CrearAlmacenResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- Editar Almacén ---
        almacenes.MapPatch("/{id:guid}", async (
            Guid id,
            EditarAlmacenCommand body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (id != body.Id) return Results.BadRequest(new { error = "ID en URL no coincide con el body" });
            await mediator.Send(body, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenAlmacenesAdministrar)
        .WithName("EditarAlmacen")
        .WithSummary("Editar almacén (F1-PR1)")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        var subs = app
            .MapGroup("/api/v1/almacen/sub-almacenes")
            .WithTags("Almacen")
            .RequireAuthorization();

        // --- Listar SubAlmacenes ---
        subs.MapGet("/", async (
            [FromQuery] Guid? almacenId,
            [FromQuery] TipoSubAlmacen? tipo,
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] string? q,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var (off, lim) = NormalizePaging(offset, limit);
            var response = await mediator.Send(
                new ListarSubAlmacenesQuery(almacenId, tipo, estatus, q, off, lim), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenAlmacenesRead)
        .WithName("ListarSubAlmacenes")
        .WithSummary("Listar sub-almacenes (F1-PR1)")
        .Produces<AlmacenPagedResponse<SubAlmacenListItem>>(StatusCodes.Status200OK);

        // --- Crear SubAlmacén ---
        subs.MapPost("/", async (
            CrearSubAlmacenCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/almacen/sub-almacenes/{response.Id}", response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenAlmacenesAdministrar)
        .WithName("CrearSubAlmacen")
        .WithSummary("Crear sub-almacén (F1-PR1)")
        .Produces<CrearSubAlmacenResponse>(StatusCodes.Status201Created);

        // --- Editar SubAlmacén ---
        subs.MapPatch("/{id:guid}", async (
            Guid id,
            EditarSubAlmacenCommand body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (id != body.Id) return Results.BadRequest(new { error = "ID en URL no coincide con el body" });
            await mediator.Send(body, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenAlmacenesAdministrar)
        .WithName("EditarSubAlmacen")
        .WithSummary("Editar sub-almacén (F1-PR1)")
        .Produces(StatusCodes.Status204NoContent);

        // ── Ubicaciones (N4) — gestión de racks/pasillos (ADR-0047 PR C7.1) ──
        // Par RBAC propio (almacen.ubicaciones.leer/administrar) + Idempotency-Key
        // en las mutaciones. El PATCH edita solo clave/nombre; la baja va por los
        // endpoints desactivar/reactivar (C7.1 commit 3) con guardrails.
        var ubic = app
            .MapGroup("/api/v1/almacen/ubicaciones")
            .WithTags("Almacen")
            .RequireAuthorization();

        // --- Listar Ubicaciones (enriquecidas con sub-almacén + almacén padres) ---
        ubic.MapGet("/", async (
            [FromQuery] Guid? subAlmacenId,
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] string? q,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var (off, lim) = NormalizePaging(offset, limit);
            var response = await mediator.Send(
                new ListarUbicacionesQuery(subAlmacenId, estatus, q, off, lim), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenUbicacionesRead)
        .WithName("ListarUbicaciones")
        .WithSummary("Listar ubicaciones N4 con clave/nombre del sub-almacén y almacén padres (ADR-0047 PR C7.1)")
        .Produces<AlmacenPagedResponse<UbicacionListItem>>(StatusCodes.Status200OK);

        // --- Crear Ubicación ---
        ubic.MapPost("/", async (
            CrearUbicacionCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/almacen/ubicaciones/{response.Id}", response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenUbicacionesAdministrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("CrearUbicacion")
        .WithSummary("Crear ubicación física N4 (rack/pasillo) en un sub-almacén (ADR-0047 PR C7.1)")
        .Produces<CrearUbicacionResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- Editar Ubicación (solo clave/nombre) ---
        ubic.MapPatch("/{id:guid}", async (
            Guid id,
            EditarUbicacionCommand body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (id != body.Id) return Results.BadRequest(new { error = "ID en URL no coincide con el body" });
            await mediator.Send(body, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenUbicacionesAdministrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("EditarUbicacion")
        .WithSummary("Editar clave/nombre de una ubicación N4 (el estatus va por desactivar/reactivar)")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- Desactivar Ubicación (doble guardrail: default + saldo) ---
        ubic.MapPost("/{id:guid}/desactivar", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new DesactivarUbicacionCommand(id), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenUbicacionesAdministrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("DesactivarUbicacion")
        .WithSummary("Desactivar ubicación N4 (bloqueada si es la default del sub-almacén o si tiene saldo > 0)")
        .Produces<UbicacionEstatusResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- Reactivar Ubicación ---
        ubic.MapPost("/{id:guid}/reactivar", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new ReactivarUbicacionCommand(id), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenUbicacionesAdministrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ReactivarUbicacion")
        .WithSummary("Reactivar una ubicación N4 desactivada")
        .Produces<UbicacionEstatusResponse>(StatusCodes.Status200OK)
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
