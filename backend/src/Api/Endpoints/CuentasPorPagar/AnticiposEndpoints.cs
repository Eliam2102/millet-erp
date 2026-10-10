using Millet.CuentasPorPagar.Application.NotaCargo;
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
    public sealed record CancelarDocumentoBody(string Motivo);
    public sealed record SerieAnticipoBody(string Serie);
    public sealed record AmortizarNcBody(Guid NotaCreditoId, int NcVersionEsperada, decimal Monto);
    public static IEndpointRouteBuilder MapAnticiposEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/cuentas-por-pagar/anticipos")
            .WithDocumentoSucursalScope("anticipo_proveedor", "cuentas_por_pagar.documentos")
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
            DocumentoSucursalScope scope,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (command.OrdenCompraId is Guid ocId)
                await scope.VerificarAsync("orden_compra", ocId, PermisosCanonicos.CuentasPorPagarDocumentosGestionarTodasSucursales, cancellationToken);
            else await scope.VerificarSucursalAsync(null, PermisosCanonicos.CuentasPorPagarDocumentosGestionarTodasSucursales, cancellationToken);
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

        group.MapPost("/{id:guid}/cancelar", async (Guid id, [FromHeader(Name = "X-Expected-Version")] int? version,
            [FromBody] CancelarDocumentoBody body, IMediator mediator, CancellationToken ct) =>
        {
            if (version is not int v) return Results.Problem(title: "X-Expected-Version requerido", statusCode: 428);
            await mediator.Send(new CancelarDocumentoP4Command(TipoDocumentoP4.Anticipo, id, v, body.Motivo), ct);
            return Results.NoContent();
        }).WithMetadata(new RequireIdempotencyKeyAttribute())
          .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarAnticiposCapturar)
          .ProducesProblem(422).ProducesProblem(409).ProducesProblem(428);
        // Configuración del proveedor por empresa: conserva sus permisos propios y no tiene sucursal.
        // La guarda del grupo solo resuelve el parámetro documental «id», nunca «proveedorId».
        group.MapGet("/serie/{proveedorId:guid}", async (Guid proveedorId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(new { Serie = await mediator.Send(new Millet.CuentasPorPagar.Application.AnticipoProveedor.ObtenerSerieAnticipoQuery(proveedorId), ct) }))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarAnticiposLeer);
        group.MapPut("/serie/{proveedorId:guid}", async (Guid proveedorId, [FromBody] SerieAnticipoBody body, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new Millet.CuentasPorPagar.Application.AnticipoProveedor.ConfigurarSerieAnticipoCommand(proveedorId, body.Serie), ct); return Results.NoContent();
        }).WithMetadata(new RequireIdempotencyKeyAttribute()).RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarAnticiposCapturar);
        group.MapPost("/{id:guid}/amortizar-nc", async (Guid id, [FromHeader(Name = "X-Expected-Version")] int? version,
            [FromBody] AmortizarNcBody body, DocumentoSucursalScope scope, IMediator mediator, CancellationToken ct) =>
        {
            await scope.VerificarAsync("nota_credito_proveedor", body.NotaCreditoId,
                PermisosCanonicos.CuentasPorPagarDocumentosGestionarTodasSucursales, ct);
            if (version is not int v) return Results.Problem(title: "X-Expected-Version requerido", statusCode: 428);
            await mediator.Send(new AmortizarAnticipoConNcCommand(id, v, body.NotaCreditoId, body.NcVersionEsperada, body.Monto), ct); return Results.NoContent();
        }).WithMetadata(new RequireIdempotencyKeyAttribute()).RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarAnticiposCapturar);

        return app;
    }
}
