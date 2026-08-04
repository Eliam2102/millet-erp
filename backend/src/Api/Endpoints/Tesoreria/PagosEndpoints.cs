using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Tesoreria.Application.Pagos;

namespace Millet.Api.Endpoints.Tesoreria;

/// <summary>
/// Endpoints HTTP del pago a proveedor (TES-PR4, §11 del 01-diseño): el
/// operador ejecuta la transferencia en la banca y aquí registra el hecho
/// bancario contra pasivos de la bandeja. Publica los eventos espejo
/// congelados hacia CxP vía outbox.
/// </summary>
public static class PagosEndpoints
{
    public static IEndpointRouteBuilder MapTesoreriaPagosEndpoints(this IEndpointRouteBuilder app)
    {
        var pagos = app
            .MapGroup("/api/v1/tesoreria/pagos")
            .WithTags("Tesoreria")
            .RequireAuthorization();

        pagos.MapPost("/", async (
            [FromBody] RegistrarPagoProveedorCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/tesoreria/movimientos/{response.MovimientoId}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaPagosAplicar)
        .WithName("RegistrarPagoProveedor")
        .Produces<PagoProveedorResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        pagos.MapPost("/{pagoId:guid}/revertir", async (
            Guid pagoId,
            [FromBody] RevertirPagoBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new RevertirPagoProveedorCommand(pagoId, body.Motivo), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaPagosRevertir)
        .WithName("RevertirPagoProveedor")
        .Produces<PagoProveedorResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    public sealed record RevertirPagoBody(string Motivo);
}
