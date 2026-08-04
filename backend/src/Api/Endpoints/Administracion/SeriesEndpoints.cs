using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Administracion.Application.Series;
using Millet.Administracion.Domain;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Administracion;

/// <summary>
/// Endpoints HTTP del CRUD de Series + reserva de folios (F-Admin-PR6.1).
///
/// <list type="bullet">
///   <item><c>GET    /api/v1/admin/series</c> — paginado, filtros
///         empresaId/tipoDocumento (permiso <c>admin.series.gestionar</c>).</item>
///   <item><c>POST   /api/v1/admin/series</c> — alta (Idempotency-Key).</item>
///   <item><c>GET    /api/v1/admin/series/{id}</c> — detalle con preview
///         del próximo folio.</item>
///   <item><c>PATCH  /api/v1/admin/series/{id}</c> — PATCH parcial
///         (Idempotency-Key).</item>
///   <item><c>POST   /api/v1/admin/series/{id}/desactivar</c> —
///         soft-delete del agregado (Idempotency-Key).</item>
///   <item><c>POST   /api/v1/admin/series/reservar</c> — reserva atómica
///         (Idempotency-Key). Endpoint interno: solo
///         <c>RequireAuthorization()</c> (cualquier usuario autenticado).</item>
/// </list>
/// </summary>
public static class SeriesEndpoints
{
    public static IEndpointRouteBuilder MapSeriesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/admin/series")
            .WithTags("Administracion");

        // --- LIST ---
        group.MapGet("/", async (
            [FromQuery] Guid? empresaId,
            [FromQuery] TipoDocumentoSerie? tipoDocumento,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ListarSeriesQuery(
                    EmpresaId: empresaId,
                    TipoDocumento: tipoDocumento,
                    Offset: offset ?? 0,
                    Limit: limit ?? 50),
                ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminSeriesGestionar)
        .WithName("ListarSeries")
        .WithSummary("Listar series de folios paginadas")
        .Produces<ListarSeriesResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // --- CREATE ---
        group.MapPost("/", async (
            [FromBody] CrearSerieCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/admin/series/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminSeriesGestionar)
        .WithName("CrearSerie")
        .WithSummary("Crear serie de folios nueva")
        .Produces<SerieResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // --- DETALLE ---
        group.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new ObtenerSerieQuery(id), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminSeriesGestionar)
        .WithName("ObtenerSerie")
        .WithSummary("Detalle de serie con preview del próximo folio")
        .Produces<SerieDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- PATCH ---
        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] ActualizarSeriePayload payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var command = new ActualizarSerieCommand(
                id,
                payload.Prefijo,
                payload.Sufijo,
                payload.ReinicioPeriodo,
                payload.LimpiarSufijo ?? false);
            var response = await mediator.Send(command, ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminSeriesGestionar)
        .WithName("ActualizarSerie")
        .WithSummary("PATCH parcial sobre serie")
        .Produces<SerieResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- DESACTIVAR ---
        group.MapPost("/{id:guid}/desactivar", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new DesactivarSerieCommand(id), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminSeriesGestionar)
        .WithName("DesactivarSerie")
        .WithSummary("Desactivar serie de folios")
        .Produces<SerieResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- RESERVAR (interno: requiere autenticación pero sin permiso específico) ---
        group.MapPost("/reservar", async (
            [FromBody] ReservarFolioCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization()
        .WithName("ReservarFolio")
        .WithSummary("Reservar atómicamente el siguiente folio de una serie")
        .Produces<ReservarFolioResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    /// <summary>
    /// Payload del PATCH /series/{id} — el caller no envía Id en el body
    /// (viene del path).
    /// </summary>
    public sealed record ActualizarSeriePayload(
        string? Prefijo,
        string? Sufijo,
        ReinicioPeriodo? ReinicioPeriodo,
        bool? LimpiarSufijo);
}
