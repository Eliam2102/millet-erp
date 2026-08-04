using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Application.Movimientos;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Api.Endpoints.Tesoreria;

/// <summary>
/// Endpoints HTTP del libro de movimientos bancarios (TES-PR2, §11 del
/// 01-diseño): bandeja paginada, detalle y alta manual de INGRESO. Los
/// egresos no tienen endpoint propio: solo entran vía pagos (PR-4) o pago
/// a cuenta (PR-6).
/// </summary>
public static class MovimientosEndpoints
{
    public static IEndpointRouteBuilder MapTesoreriaMovimientosEndpoints(this IEndpointRouteBuilder app)
    {
        var movimientos = app
            .MapGroup("/api/v1/tesoreria/movimientos")
            .WithTags("Tesoreria")
            .RequireAuthorization();

        movimientos.MapGet("/", async (
            [FromQuery] Guid? cuentaBancariaId,
            [FromQuery] SentidoMovimiento? sentido,
            [FromQuery] EstadoAplicacionMovimiento? estadoAplicacion,
            [FromQuery] EstadoConciliacionMovimiento? estadoConciliacion,
            [FromQuery] DateOnly? desde,
            [FromQuery] DateOnly? hasta,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new MovimientosBancariosQuery(
                    cuentaBancariaId, sentido, estadoAplicacion, estadoConciliacion,
                    desde, hasta, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaMovimientosVer)
        .WithName("ListarMovimientosBancarios")
        .Produces<PagedResponse<MovimientoBancarioResponse>>(StatusCodes.Status200OK);

        movimientos.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new MovimientoDetalleQuery(id), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaMovimientosVer)
        .WithName("ObtenerMovimientoBancario")
        .Produces<MovimientoDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        movimientos.MapPost("/", async (
            [FromBody] RegistrarMovimientoIngresoCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/tesoreria/movimientos/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaMovimientosRegistrar)
        .WithName("RegistrarMovimientoIngreso")
        .Produces<MovimientoBancarioResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
