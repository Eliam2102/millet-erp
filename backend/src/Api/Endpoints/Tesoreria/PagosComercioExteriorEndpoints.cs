using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Web;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Application.Pasivos;

namespace Millet.Api.Endpoints.Tesoreria;

/// <summary>
/// I13.3 (86e3ke4cx): bandeja por rol y firmas de nivel 1 / nivel 2 de los pagos de comercio exterior. La facultad
/// de cada firma depende del tipo (importación / exportación) y del nivel, así que la valida el handler.
/// </summary>
public static class PagosComercioExteriorEndpoints
{
    public sealed record RechazoPagoCeBody(string Motivo);

    public static IEndpointRouteBuilder MapTesoreriaPagosComercioExteriorEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/tesoreria/pagos-comercio-exterior").WithTags("Tesoreria").RequireAuthorization();

        g.MapGet("/", async ([FromQuery] string? estado, [FromQuery] bool? soloMiNivel, [FromQuery] int? offset, [FromQuery] int? limit,
            IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarAutorizacionesPagoComercioExteriorQuery(estado, soloMiNivel ?? false, offset ?? 0, limit ?? 50), ct)))
            .WithName("ListarAutorizacionesPagoComercioExterior")
            .Produces<PagedResponse<AutorizacionPagoComercioExteriorResponse>>();

        g.MapPost("/{facturaProveedorId:guid}/autorizar/{nivel:int}", async (Guid facturaProveedorId, int nivel, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new AutorizarPagoComercioExteriorCommand(facturaProveedorId, nivel), ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .WithName("AutorizarPagoComercioExterior")
            .Produces<AutorizacionPagoComercioExteriorResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        g.MapPost("/{facturaProveedorId:guid}/rechazar", async (Guid facturaProveedorId, [FromBody] RechazoPagoCeBody body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new RechazarPagoComercioExteriorCommand(facturaProveedorId, body.Motivo), ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .WithName("RechazarPagoComercioExterior")
            .Produces<AutorizacionPagoComercioExteriorResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
