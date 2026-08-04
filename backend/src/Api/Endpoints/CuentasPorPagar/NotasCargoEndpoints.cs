using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
    public static IEndpointRouteBuilder MapNotasCargoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/cuentas-por-pagar/notas-cargo")
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
            CancellationToken cancellationToken) =>
        {
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

            var response = await mediator.Send(new AplicarNotaCargoCommand(id, v), cancellationToken);
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

        return app;

    }
}
