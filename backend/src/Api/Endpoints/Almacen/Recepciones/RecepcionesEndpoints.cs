using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Movimientos;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Almacen.Recepciones;

/// <summary>
/// Endpoints de Recepciones del módulo Almacén (F2-PR2). Bajo
/// <c>/api/v1/almacen/recepciones</c>. Auth con permisos canónicos
/// <c>almacen.entradas.*</c>. POST de captura+registro requiere
/// Idempotency-Key (ADR-0020).
/// </summary>
public static class RecepcionesEndpoints
{
    private const int LimitMax = 500;

    public static IEndpointRouteBuilder MapRecepcionesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/almacen/recepciones")
            .WithTags("Almacen")
            .RequireAuthorization();

        // --- Listar Recepciones (bandeja) ---
        group.MapGet("/", async (
            [FromQuery] EstadoMovimiento? estado,
            [FromQuery] Guid? subAlmacenId,
            [FromQuery] Guid? ordenCompraId,
            [FromQuery] DateOnly? desde,
            [FromQuery] DateOnly? hasta,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var (off, lim) = NormalizePaging(offset, limit);
            var response = await mediator.Send(
                new ListarRecepcionesQuery(estado, subAlmacenId, ordenCompraId, desde, hasta, off, lim), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenEntradasLeer)
        .WithName("ListarRecepciones")
        .WithSummary("Listar recepciones (bandeja con filtros estado/sub-almacén/OC/fecha)")
        .Produces<AlmacenPagedResponse<RecepcionListItem>>(StatusCodes.Status200OK);

        // --- Obtener Recepción por Id ---
        group.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var detalle = await mediator.Send(new ObtenerRecepcionPorIdQuery(id), ct);
            return detalle is null ? Results.NotFound() : Results.Ok(detalle);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenEntradasLeer)
        .WithName("ObtenerRecepcionPorId")
        .WithSummary("Obtener recepción con líneas")
        .Produces<RecepcionDetalle>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- Registrar Recepción Variante A (factura/CFDI) ---
        group.MapPost("/", async (
            RegistrarRecepcionConFacturaCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created(
                $"/api/v1/almacen/recepciones/{response.RecepcionId}",
                response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenEntradasRegistrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("RegistrarRecepcionConFactura")
        .WithSummary("Registrar recepción Variante A (con factura/CFDI conocido)")
        .Produces<RegistrarRecepcionResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- Registrar Recepción Variante B (packing list, factura pendiente) ---
        group.MapPost("/packing-list", async (
            RegistrarRecepcionConPackingListCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created(
                $"/api/v1/almacen/recepciones/{response.RecepcionId}",
                response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenEntradasRegistrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("RegistrarRecepcionConPackingList")
        .WithSummary("Registrar recepción Variante B (materiales directos no-vidrio, factura pendiente)")
        .Produces<RegistrarRecepcionResponse>(StatusCodes.Status201Created)
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
