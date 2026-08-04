using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorPagar.Application.AnticipoProveedor.CapturarAnticipo;
using Millet.CuentasPorPagar.Application.AnticipoProveedor.Queries;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Domain.AnticipoProveedor;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorPagar;

/// <summary>
/// Endpoints HTTP del agregado <c>AnticipoProveedor</c> (F6-PR2). Cubre
/// la captura del anticipo (CFDI con serie FANT) y bandeja paginada.
/// La amortización contra una factura final usa el endpoint
/// <c>POST /api/v1/cuentas-por-pagar/facturas/{id}/aplicar-anticipo</c>
/// expuesto en <see cref="FacturasEndpoints"/>.
/// </summary>
public static class AnticiposEndpoints
{
    public static IEndpointRouteBuilder MapAnticiposEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/cuentas-por-pagar/anticipos")
            .WithTags("CuentasPorPagar")
            .RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery] EstadoAnticipo? estado,
            [FromQuery] Guid? proveedorId,
            [FromQuery] Guid? ordenCompraId,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarAnticiposQuery(estado, proveedorId, ordenCompraId, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarAnticiposLeer)
        .WithName("ListarAnticiposProveedor")
        .Produces<PagedResponse<AnticipoListItemResponse>>(StatusCodes.Status200OK);

        group.MapPost("/", async (
            [FromBody] CapturarAnticipoCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-pagar/anticipos/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarAnticiposCapturar)
        .WithName("CapturarAnticipoProveedor")
        .WithSummary("Captura un anticipo a proveedor (CFDI serie FANT)")
        .WithDescription(
            "Persiste el CFDI con serie FANT como un anticipo abierto y publica el " +
            "evento `cxp.anticipo_proveedor.capturado.v1` para que Compras lo asocie " +
            "a la OC referenciada. Idempotency-Key obligatorio.")
        .Produces<CapturarAnticipoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
