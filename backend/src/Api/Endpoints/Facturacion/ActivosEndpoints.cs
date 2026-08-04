using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Facturacion.Application.Activos.AutorizarVentaActivo;
using Millet.Facturacion.Application.Activos.Queries;
using Millet.Facturacion.Domain.Activos;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>
/// Endpoints de venta de activos fijos del módulo Facturación (F9). La
/// autorización del Contador General es previa a la emisión.
/// <c>/api/v1/facturacion/activos</c>.
/// </summary>
public static class ActivosEndpoints
{
    public static IEndpointRouteBuilder MapFacturacionActivosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/facturacion/activos")
            .WithTags("Facturacion");

        // POST: el Contador General autoriza la venta de un activo fijo (previo a emitir).
        group.MapPost("/autorizar", async (
            AutorizarVentaActivoCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/facturacion/activos/{response.AutorizacionId}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionActivosAutorizar)
        .WithName("AutorizarVentaActivo")
        .WithSummary("Autoriza la venta de un activo fijo (Contador General)")
        .Produces<AutorizarVentaActivoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // GET: bandeja de autorizaciones (por defecto las Autorizadas pendientes de usar). B13.
        group.MapGet("/autorizaciones", async (
            [FromQuery] EstadoAutorizacionActivo? estado, IMediator mediator, CancellationToken ct) =>
        {
            var items = await mediator.Send(new BandejaAutorizacionesActivoQuery(estado), ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionActivosAutorizar)
        .WithName("BandejaAutorizacionesActivo")
        .WithSummary("Bandeja de autorizaciones de venta de activos (Contador General)")
        .Produces<IReadOnlyList<AutorizacionActivoItem>>(StatusCodes.Status200OK);

        return app;
    }
}
