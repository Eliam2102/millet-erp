using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Domain.Movimientos;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Almacen.Salidas;

/// <summary>
/// Endpoints HTTP de salidas del módulo Almacén (F4-PR1). Bajo
/// <c>/api/v1/almacen/salidas</c>. Auth con permisos canónicos
/// <c>almacen.salidas.*</c>. POST requiere Idempotency-Key.
/// </summary>
public static class SalidasEndpoints
{
    private const int LimitMax = 500;

    public static IEndpointRouteBuilder MapSalidasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/almacen/salidas")
            .WithTags("Almacen")
            .RequireAuthorization();

        // --- Listar Salidas (bandeja) ---
        group.MapGet("/", async (
            [FromQuery] EstadoMovimiento? estado,
            [FromQuery] Guid? subAlmacenId,
            [FromQuery] Guid? rqId,
            [FromQuery] Guid? personaDestinatariaId,
            [FromQuery] DateOnly? desde,
            [FromQuery] DateOnly? hasta,
            [FromQuery] bool? soloVales,
            [FromQuery] bool? noRegularizados,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var (off, lim) = NormalizePaging(offset, limit);
            var response = await mediator.Send(
                new ListarSalidasQuery(estado, subAlmacenId, rqId, personaDestinatariaId,
                    desde, hasta, soloVales, noRegularizados, off, lim), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasLeerTodas)
        .WithName("ListarSalidas")
        .WithSummary("Listar salidas (bandeja con filtros)")
        .Produces<AlmacenPagedResponse<SalidaListItem>>(StatusCodes.Status200OK);

        // --- Obtener Salida por Id ---
        group.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var detalle = await mediator.Send(new ObtenerSalidaPorIdQuery(id), ct);
            return detalle is null ? Results.NotFound() : Results.Ok(detalle);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasLeerTodas)
        .WithName("ObtenerSalidaPorId")
        .WithSummary("Obtener salida con líneas")
        .Produces<SalidaDetalle>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- Registrar Salida Variante A (con RQ) ---
        group.MapPost("/", async (
            RegistrarSalidaConRequisicionCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created(
                $"/api/v1/almacen/salidas/{response.SalidaId}",
                response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasRegistrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("RegistrarSalidaConRequisicion")
        .WithSummary("Registrar salida normal con RQ (consume reserva si existe)")
        .Produces<RegistrarSalidaResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
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
