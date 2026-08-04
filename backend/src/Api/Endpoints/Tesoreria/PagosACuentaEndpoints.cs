using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Application.Pagos;
using Millet.Tesoreria.Application.PagosACuenta;

namespace Millet.Api.Endpoints.Tesoreria;

/// <summary>
/// Endpoints HTTP del pago a cuenta (TES-PR6, §3.4 / TES-2): registro del
/// egreso sin documento (gate RN-2), liga tardía sin re-desembolso y read
/// model de abiertos con antigüedad.
/// </summary>
public static class PagosACuentaEndpoints
{
    public static IEndpointRouteBuilder MapTesoreriaPagosACuentaEndpoints(this IEndpointRouteBuilder app)
    {
        var pagosCuenta = app
            .MapGroup("/api/v1/tesoreria/pagos-cuenta")
            .WithTags("Tesoreria")
            .RequireAuthorization();

        pagosCuenta.MapGet("/", async (
            [FromQuery] Guid? proveedorId,
            [FromQuery] bool? incluirParciales,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new PagosACuentaAbiertosQuery(proveedorId, incluirParciales ?? true, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaMovimientosVer)
        .WithName("ListarPagosACuentaAbiertos")
        .Produces<PagedResponse<PagoACuentaAbiertoResponse>>(StatusCodes.Status200OK);

        pagosCuenta.MapPost("/", async (
            [FromBody] RegistrarPagoACuentaCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/tesoreria/movimientos/{response.MovimientoId}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaPagosCuentaRegistrar)
        .WithName("RegistrarPagoACuenta")
        .Produces<PagoACuentaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        pagosCuenta.MapPost("/{movimientoId:guid}/ligar", async (
            Guid movimientoId,
            [FromBody] LigarPagoACuentaBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new LigarPagoACuentaCommand(movimientoId, body.FacturaProveedorId, body.Importe),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaPagosCuentaLigar)
        .WithName("LigarPagoACuenta")
        .Produces<PagoProveedorResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    public sealed record LigarPagoACuentaBody(Guid FacturaProveedorId, decimal Importe);
}
