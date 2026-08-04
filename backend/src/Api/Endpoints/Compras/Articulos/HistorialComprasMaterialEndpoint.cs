using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Compras.Application.Oc.ListarUltimas100Compras;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Compras.Articulos;

/// <summary>
/// Endpoint del historial de compras por artículo (F7-PR3, §8.5). Vive
/// bajo <c>/api/v1/compras/articulos/...</c> para diferenciar de
/// <c>/ordenes/...</c>. Devuelve las últimas 100 líneas de OC que
/// referencian el artículo.
/// </summary>
public static class HistorialComprasMaterialEndpoint
{
    public static IEndpointRouteBuilder MapHistorialComprasMaterialEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/compras/articulos").WithTags("Compras");

        group.MapGet("/{id:guid}/historial-compras", async (
            Guid id,
            IMediator mediator,
            [FromQuery] Guid? proveedorId,
            [FromQuery] DateOnly? fechaDesde,
            [FromQuery] decimal? cantidadMinima,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarUltimas100ComprasQuery(
                    ArticuloId: id,
                    ProveedorId: proveedorId,
                    FechaDesde: fechaDesde,
                    CantidadMinima: cantidadMinima),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesLeer)
        .WithName("ObtenerHistorialComprasMaterial")
        .WithSummary("Últimas 100 compras de un artículo (F7-PR3)")
        .WithDescription(
            "Devuelve las últimas 100 líneas de OC para el artículo indicado, " +
            "ordenadas por fecha documento DESC. Filtros opcionales: proveedor, " +
            "fecha desde, cantidad mínima. Útil para análisis de tendencia de " +
            "precios y proveedores.")
        .Produces<ListarUltimas100ComprasResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
