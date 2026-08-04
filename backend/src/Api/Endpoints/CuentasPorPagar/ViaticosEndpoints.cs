using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Application.Viaticos.CapturarComprobacion;
using Millet.CuentasPorPagar.Application.Viaticos.LiberarComprobacion;
using Millet.CuentasPorPagar.Application.Viaticos.Queries;
using Millet.CuentasPorPagar.Application.Viaticos.SolicitarAnticipo;
using Millet.CuentasPorPagar.Application.Viaticos.Transiciones;
using Millet.CuentasPorPagar.Domain.Viaticos;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorPagar;

/// <summary>
/// Endpoints del flujo de viáticos electrónicos (§7.4.2, F7-PR3).
/// Cubre todo el ciclo end-to-end: solicitud → autorización (jefe + DF
/// si excede) → anticipo (Tesorería) → comprobación al regreso →
/// liberación (CxP) con generación de facturas.
/// </summary>
public static class ViaticosEndpoints
{
    public static IEndpointRouteBuilder MapViaticosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/cuentas-por-pagar/viaticos")
            .WithTags("CuentasPorPagar")
            .RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery] EstadoSolicitudViaticos? estado,
            [FromQuery] Guid? empleadoId,
            [FromQuery] Guid? jefeDirectoId,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarSolicitudesViaticosQuery(estado, empleadoId, jefeDirectoId, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarViaticosLeer)
        .WithName("ListarSolicitudesViaticos")
        .Produces<PagedResponse<SolicitudViaticosListItemResponse>>(StatusCodes.Status200OK);

        group.MapPost("/", async (
            [FromBody] SolicitarAnticipoViaticosCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-pagar/viaticos/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarViaticosSolicitar)
        .WithName("SolicitarAnticipoViaticos")
        .WithSummary("Empleado solicita anticipo de viáticos — valida contra política por puesto+destino")
        .Produces<SolicitarAnticipoViaticosResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/autorizar-jefe", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(new AutorizarPorJefeViaticosCommand(id, v), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarViaticosAutorizarJefe)
        .WithName("AutorizarPorJefeViaticos")
        .WithSummary("Jefe directo autoriza la solicitud — escala a DF si excede política")
        .Produces<TransicionViaticosResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        group.MapPost("/{id:guid}/autorizar-df", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(new AutorizarPorDfViaticosCommand(id, v), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarViaticosAutorizarDf)
        .WithName("AutorizarPorDfViaticos")
        .WithSummary("Dirección de Finanzas autoriza solicitud que excede política")
        .Produces<TransicionViaticosResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        group.MapPost("/{id:guid}/marcar-pagado", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(new MarcarAnticipoPagadoViaticosCommand(id, v), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarViaticosMarcarPagado)
        .WithName("MarcarAnticipoPagadoViaticos")
        .Produces<TransicionViaticosResponse>(StatusCodes.Status200OK);

        group.MapPost("/{id:guid}/capturar-comprobacion", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] CapturarComprobacionBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new CapturarComprobacionViaticosCommand(id, v, body.Lineas),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarViaticosCapturarComprobacion)
        .WithName("CapturarComprobacionViaticos")
        .Produces<CapturarComprobacionViaticosResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem();

        group.MapPost("/{id:guid}/liberar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(new LiberarComprobacionViaticosCommand(id, v), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarViaticosLiberar)
        .WithName("LiberarComprobacionViaticos")
        .WithSummary("CxP libera la comprobación — genera facturas y calcula diferencia de liquidación")
        .Produces<LiberarComprobacionViaticosResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/rechazar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] RechazarBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new RechazarSolicitudViaticosCommand(id, v, body.Motivo), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarViaticosAutorizarJefe)
        .WithName("RechazarSolicitudViaticos")
        .Produces<TransicionViaticosResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem();

        // Serie detalles CxP: detalle completo para el master-detail del FE.
        group.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new ObtenerSolicitudViaticosQuery(id), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarViaticosLeer)
        .WithName("ObtenerSolicitudViaticos")
        .WithSummary("Detalle de solicitud de viaticos con lineas de comprobacion")
        .Produces<SolicitudViaticosDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;

    }

    public sealed record CapturarComprobacionBody(IReadOnlyList<CapturarLineaViaticosInput> Lineas);

    public sealed record RechazarBody(string Motivo);
}
