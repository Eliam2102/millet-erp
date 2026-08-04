using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Application.ComprobacionGastos.Aduanales;
using Millet.CuentasPorPagar.Application.ComprobacionGastos.CrearComprobacionAduanales;
using Millet.CuentasPorPagar.Application.ComprobacionGastos.CrearComprobacionCajaChica;
using Millet.CuentasPorPagar.Application.ComprobacionGastos.Queries;
using Millet.CuentasPorPagar.Application.ComprobacionGastos.Transiciones;
using Millet.CuentasPorPagar.Domain.ComprobacionGastos;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorPagar;

/// <summary>
/// Endpoints HTTP del agregado <c>ComprobacionGastos</c> (F7-PR1). Cubre
/// la variante <b>Caja Chica</b> completa: captura con N CFDIs que
/// generan facturas individuales, transiciones de ciclo
/// (Borrador → PorRevisar → Autorizada → Aplicada) y rechazo. Variantes
/// Aduanales/Viáticos/TC entran en F7-PR2/PR3.
/// </summary>
public static class ComprobacionesEndpoints
{
    public static IEndpointRouteBuilder MapComprobacionesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/cuentas-por-pagar/comprobaciones")
            .WithTags("CuentasPorPagar")
            .RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery] TipoComprobacionGastos? tipo,
            [FromQuery] EstadoComprobacionGastos? estado,
            [FromQuery] Guid? sucursalId,
            [FromQuery] Guid? responsableId,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarComprobacionesGastosQuery(tipo, estado, sucursalId, responsableId, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarComprobacionesLeer)
        .WithName("ListarComprobacionesGastos")
        .Produces<PagedResponse<ComprobacionGastosListItemResponse>>(StatusCodes.Status200OK);

        group.MapPost("/caja-chica", async (
            [FromBody] CrearComprobacionCajaChicaCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-pagar/comprobaciones/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarComprobacionesCapturar)
        .WithName("CrearComprobacionCajaChica")
        .WithSummary("Captura una comprobación de Caja Chica con N CFDIs")
        .WithDescription(
            "Cada CFDI del payload se persiste como una FacturaProveedor sin OC y se " +
            "liga vía LineaComprobacionGastos. Idempotency-Key obligatorio.")
        .Produces<CrearComprobacionCajaChicaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/enviar-revision", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(new EnviarComprobacionARevisionCommand(id, v), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarComprobacionesCapturar)
        .WithName("EnviarComprobacionARevision")
        .Produces<TransicionComprobacionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        group.MapPost("/{id:guid}/autorizar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(new AutorizarComprobacionGastosCommand(id, v), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarComprobacionesAprobarNivel1)
        .WithName("AutorizarComprobacionGastos")
        .WithSummary("Autoriza una comprobación (responsable de sucursal / nivel 1)")
        .Produces<TransicionComprobacionResponse>(StatusCodes.Status200OK)
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
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(new AplicarComprobacionGastosCommand(id, v), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarComprobacionesAprobarNivel1)
        .WithName("AplicarComprobacionGastos")
        .Produces<TransicionComprobacionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        // -------- F7-PR2: Aduanales con doble firma --------

        group.MapPost("/aduanales", async (
            [FromBody] CrearComprobacionAduanalesCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-pagar/comprobaciones/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarComprobacionesCapturar)
        .WithName("CrearComprobacionAduanales")
        .WithSummary("Captura una comprobación de Gastos Aduanales agrupando facturas con OC ya capturadas")
        .WithDescription(
            "Las facturas de la agencia aduanal deben estar ya capturadas vía CapturarFacturaConOcCommand " +
            "(en estado Capturada y con OC). Idempotency-Key obligatorio.")
        .Produces<CrearComprobacionAduanalesResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/autorizar-nivel1", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(new AutorizarNivel1AduanalesCommand(id, v), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarComprobacionesAprobarNivel1)
        .WithName("AutorizarNivel1Aduanales")
        .WithSummary("Firma Nivel 1 (Comercio Exterior) — habilita registro del pasivo")
        .Produces<TransicionComprobacionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        group.MapPost("/{id:guid}/autorizar-nivel2", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(new AutorizarNivel2AduanalesCommand(id, v), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarComprobacionesAprobarNivel2)
        .WithName("AutorizarNivel2Aduanales")
        .WithSummary("Firma Nivel 2 (Dirección de Finanzas) — habilita pago y autoriza facturas ligadas")
        .Produces<TransicionComprobacionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        // -------- Common: Rechazar --------

        group.MapPost("/{id:guid}/rechazar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] RechazarRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new RechazarComprobacionGastosCommand(id, v, request.Motivo), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarComprobacionesAprobarNivel1)
        .WithName("RechazarComprobacionGastos")
        .Produces<TransicionComprobacionResponse>(StatusCodes.Status200OK)
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
            var response = await mediator.Send(new ObtenerComprobacionGastosQuery(id), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarComprobacionesLeer)
        .WithName("ObtenerComprobacionGastos")
        .WithSummary("Detalle de comprobacion de gastos con lineas")
        .Produces<ComprobacionGastosDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;

    }

    public sealed record RechazarRequest(string Motivo);
}
