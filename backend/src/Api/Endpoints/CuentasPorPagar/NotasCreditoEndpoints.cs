using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Application.NotaCreditoProveedor.CapturarNotaCredito;
using Millet.CuentasPorPagar.Application.NotaCreditoProveedor.Queries;
using Millet.CuentasPorPagar.Application.NotaCreditoProveedor.VincularFactura;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorPagar;

/// <summary>
/// Endpoints HTTP del agregado <c>NotaCreditoProveedor</c> (F6-PR1).
/// Cubre captura + bandeja + vinculación manual a factura origen.
/// La aplicación al saldo de la factura (decrementar
/// <c>NcAplicadasTotal</c>) entra en F6-PR2
/// (<c>AplicarNotaCreditoAFacturaCommand</c>).
/// </summary>
public static class NotasCreditoEndpoints
{
    public static IEndpointRouteBuilder MapNotasCreditoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/cuentas-por-pagar/notas-credito")
            .WithTags("CuentasPorPagar")
            .RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery] EstadoNotaCredito? estado,
            [FromQuery] Guid? proveedorId,
            [FromQuery] Guid? facturaOrigenId,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarNotasCreditoQuery(estado, proveedorId, facturaOrigenId, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarNotasCreditoLeer)
        .WithName("ListarNotasCredito")
        .Produces<PagedResponse<NotaCreditoListItemResponse>>(StatusCodes.Status200OK);

        group.MapPost("/", async (
            [FromBody] CapturarNotaCreditoCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-pagar/notas-credito/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarNotasCreditoCapturar)
        .WithName("CapturarNotaCredito")
        .WithSummary("Captura una NC del proveedor (relación CFDI 01/03/07)")
        .WithDescription(
            "Resuelve automáticamente la factura origen por `UuidRelacionCfdi`+`ProveedorId`. " +
            "Si no encuentra match, la NC queda en estado `EnEspera` y el " +
            "`NotaCreditoEnEsperaMatchWorker` la vincula cuando llegue la factura. " +
            "Idempotency-Key obligatorio.")
        .Produces<CapturarNotaCreditoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/vincular-factura", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] VincularFacturaRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
            {
                return Results.Problem(
                    title: "X-Expected-Version requerido",
                    statusCode: StatusCodes.Status428PreconditionRequired);
            }

            var response = await mediator.Send(
                new VincularFacturaNotaCreditoCommand(id, v, request.FacturaOrigenId),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarNotasCreditoCapturar)
        .WithName("VincularFacturaNotaCredito")
        .WithSummary("Vincula manualmente una NC EnEspera a su factura origen")
        .Produces<VincularFacturaNotaCreditoResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        // Serie detalles CxP: detalle completo para el master-detail del FE.
        group.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new ObtenerNotaCreditoQuery(id), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarNotasCreditoLeer)
        .WithName("ObtenerNotaCredito")
        .WithSummary("Detalle de nota de credito")
        .Produces<NotaCreditoDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;

    }

    public sealed record VincularFacturaRequest(Guid FacturaOrigenId);
}
