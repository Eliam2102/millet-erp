using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Compras.Application.Trazabilidad.ObtenerArbolDocumentos;
using Millet.Compras.Domain.Trazabilidad;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Compras.Trazabilidad;

/// <summary>
/// Endpoint cross-módulo de trazabilidad documental (F7-PR2). Construye
/// el árbol de documentos (RQ → OC → ... → pago) desde un nodo origen.
/// Vive bajo <c>/api/v1/compras/trazabilidad</c> porque OC es el módulo
/// dueño del servicio en v1; cuando otros módulos sumen providers,
/// el endpoint sigue siendo el mismo.
/// </summary>
public static class ArbolDocumentosEndpoint
{
    public static IEndpointRouteBuilder MapArbolDocumentosEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/compras/trazabilidad").WithTags("Compras");

        group.MapGet("/arbol-documentos", async (
            [FromQuery] TipoDocumentoTrazabilidad desde,
            [FromQuery] Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var arbol = await mediator.Send(new ObtenerArbolDocumentosQuery(desde, id), cancellationToken);
            return arbol is null ? Results.NotFound() : Results.Ok(arbol);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesLeer)
        .WithName("ObtenerArbolDocumentos")
        .WithSummary("Árbol de trazabilidad RQ → OC → ... (F7-PR2)")
        .WithDescription(
            "Construye el árbol cross-módulo de documentos relacionados " +
            "desde el nodo origen. V1 conoce RQ y OC: desde una RQ devuelve " +
            "las OCs descendientes; desde una OC devuelve las RQs ascendentes. " +
            "CxP/Recepción/Tesorería se incorporan cuando los módulos implementen " +
            "su provider del servicio.")
        .Produces<NodoArbolDocumento>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
