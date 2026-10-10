using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Application.NotaCargo;
using Millet.CuentasPorPagar.Application.NotaCargo.Queries;
using Millet.CuentasPorPagar.Domain.NotaCargo;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorPagar;

/// <summary>
/// Endpoints HTTP del agregado <c>NotaCargo</c> (F6-PR2). Cubre el ciclo
/// <c>Borrador → Autorizada → Aplicada</c>. Formalización con NC fiscal
/// del proveedor entra en F6-PR3.
/// </summary>
public static class NotasCargoEndpoints
{
    public sealed record AplicarNotaCargoBody(Guid? FacturaOrigenId);
    public sealed record CancelarDocumentoBody(string Motivo);
    public sealed record FormalizarCargoBody(Guid NotaCreditoId);
    public static IEndpointRouteBuilder MapNotasCargoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/cuentas-por-pagar/notas-cargo")
            .WithDocumentoSucursalScope("nota_cargo", "cuentas_por_pagar.documentos")
            .WithTags("CuentasPorPagar")
            .RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery] EstadoNotaCargo? estado,
            [FromQuery] Guid? proveedorId,
            [FromQuery] Guid? facturaOrigenId,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarNotasCargoQuery(estado, proveedorId, facturaOrigenId, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarNotasCargoLeer)
        .WithName("ListarNotasCargo")
        .Produces<PagedResponse<NotaCargoListItemResponse>>(StatusCodes.Status200OK);

        group.MapPost("/", async (
            [FromBody] CrearNotaCargoCommand command,
            IMediator mediator,
            DocumentoSucursalScope scope,
            CancellationToken cancellationToken) =>
        {
            await scope.VerificarSucursalAsync(command.SucursalId, "cuentas_por_pagar.documentos.gestionar-todas-sucursales", cancellationToken);
            if (command.FacturaOrigenId is Guid facturaId)
                await scope.VerificarAsync("factura_proveedor", facturaId,
                    PermisosCanonicos.CuentasPorPagarFacturasGestionarTodasSucursales, cancellationToken);
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-pagar/notas-cargo/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarNotasCargoCrear)
        .WithName("CrearNotaCargo")
        .WithSummary("Crea una nota de cargo en estado Borrador con folio NCG atómico")
        .WithDescription(
            "Asigna folio interno NCG-YYYY-NNNNNN de forma atómica vía upsert sobre " +
            "`folio_secuencias_nota_cargo` por (empresa, año). Idempotency-Key obligatorio.")
        .Produces<CrearNotaCargoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/autorizar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
            {
                return Results.Problem(
                    title: "X-Expected-Version requerido",
                    statusCode: StatusCodes.Status428PreconditionRequired);
            }

            var response = await mediator.Send(new AutorizarNotaCargoCommand(id, v), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarNotasCargoAutorizar)
        .WithName("AutorizarNotaCargo")
        .WithSummary("Autoriza una nota de cargo en estado Borrador y publica el evento de integración")
        .Produces<AutorizarNotaCargoResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        group.MapPost("/{id:guid}/aplicar", async (
            Guid id,
            [FromBody] AplicarNotaCargoBody? body,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            CuentasPorPagarDbContext scopeDb,
            DocumentoSucursalScope scope,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            // P4 puede elegir una factura al aplicar; si se omite, modifica la factura ya vinculada.
            var facturaId = body?.FacturaOrigenId ?? await scopeDb.NotasCargo.AsNoTracking()
                .Where(x => x.Id == id).Select(x => x.FacturaOrigenId).FirstOrDefaultAsync(cancellationToken);
            if (facturaId is Guid factura)
                await scope.VerificarAsync("factura_proveedor", factura,
                    PermisosCanonicos.CuentasPorPagarFacturasGestionarTodasSucursales, cancellationToken);
            if (expectedVersion is not int v)
            {
                return Results.Problem(
                    title: "X-Expected-Version requerido",
                    statusCode: StatusCodes.Status428PreconditionRequired);
            }

            var response = await mediator.Send(new AplicarNotaCargoCommand(id, v, body?.FacturaOrigenId), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarNotasCargoAplicar)
        .WithName("AplicarNotaCargo")
        .WithSummary("Aplica una nota de cargo Autorizada (deducción contable interna)")
        .Produces<AplicarNotaCargoResponse>(StatusCodes.Status200OK)
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
            var response = await mediator.Send(new ObtenerNotaCargoQuery(id), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarNotasCargoLeer)
        .WithName("ObtenerNotaCargo")
        .WithSummary("Detalle de nota de cargo")
        .Produces<NotaCargoDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/cancelar", async (Guid id, [FromHeader(Name = "X-Expected-Version")] int? version,
            [FromBody] CancelarDocumentoBody body, IMediator mediator, CancellationToken ct) =>
        {
            if (version is not int v) return Results.Problem(title: "X-Expected-Version requerido", statusCode: 428);
            await mediator.Send(new CancelarDocumentoP4Command(TipoDocumentoP4.NotaCargo, id, v, body.Motivo), ct);
            return Results.NoContent();
        }).WithMetadata(new RequireIdempotencyKeyAttribute())
          .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarNotasCargoCrear)
          .ProducesProblem(422).ProducesProblem(409).ProducesProblem(428);
        group.MapPost("/{id:guid}/formalizar", async (Guid id, [FromHeader(Name = "X-Expected-Version")] int? version,
            [FromBody] FormalizarCargoBody body, DocumentoSucursalScope scope, IMediator mediator, CancellationToken ct) =>
        {
            await scope.VerificarAsync("nota_credito_proveedor", body.NotaCreditoId,
                PermisosCanonicos.CuentasPorPagarDocumentosGestionarTodasSucursales, ct);
            if (version is not int v) return Results.Problem(title: "X-Expected-Version requerido", statusCode: 428);
            await mediator.Send(new FormalizarNotaCargoCommand(id, v, body.NotaCreditoId), ct); return Results.NoContent();
        }).WithMetadata(new RequireIdempotencyKeyAttribute())
          .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarNotasCargoAplicar).ProducesProblem(422);

        return app;

    }
}
