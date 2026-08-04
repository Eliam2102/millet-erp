using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorCobrar.Application.Common;
using Millet.CuentasPorCobrar.Application.LineasCredito;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorCobrar;

/// <summary>
/// Endpoints HTTP del master de líneas de crédito (CXC-PR1, §11 del
/// 01-diseño). CRUD acotado: crear, editar límite/plazo, bloquear y
/// desbloquear + bandeja paginada. El crédito disponible entra en
/// CXC-PR2; liberaciones y overrides en CXC-PR4.
/// </summary>
public static class LineasCreditoEndpoints
{
    public static IEndpointRouteBuilder MapLineasCreditoEndpoints(this IEndpointRouteBuilder app)
    {
        var lineas = app
            .MapGroup("/api/v1/cuentas-por-cobrar/lineas-credito")
            .WithTags("CuentasPorCobrar")
            .RequireAuthorization();

        lineas.MapGet("/", async (
            [FromQuery] Guid? clienteId,
            [FromQuery] EstadoLineaCredito? estado,
            [FromQuery] string? moneda,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarLineasCreditoQuery(clienteId, estado, moneda, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarLineasCreditoLeer)
        .WithName("ListarLineasCredito")
        .Produces<PagedResponse<LineaCreditoResponse>>(StatusCodes.Status200OK);

        lineas.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new ObtenerLineaCreditoQuery(id), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarLineasCreditoLeer)
        .WithName("ObtenerLineaCredito")
        .Produces<LineaCreditoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        lineas.MapPost("/", async (
            [FromBody] CrearLineaCreditoCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-cobrar/lineas-credito/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarLineasCreditoGestionar)
        .WithName("CrearLineaCredito")
        .Produces<LineaCreditoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        lineas.MapPut("/{id:guid}", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] ActualizarLineaCreditoBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new ActualizarLineaCreditoCommand(id, v, body.Limite, body.PlazoDias, body.Clasificacion),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarLineasCreditoGestionar)
        .WithName("ActualizarLineaCredito")
        .Produces<LineaCreditoResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound);

        lineas.MapPost("/{id:guid}/bloquear", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] BloquearBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new BloquearLineaCreditoCommand(id, v, body.Motivo), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarLineasCreditoGestionar)
        .WithName("BloquearLineaCredito")
        .Produces<LineaCreditoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        lineas.MapPost("/{id:guid}/desbloquear", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(new DesbloquearLineaCreditoCommand(id, v), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarLineasCreditoGestionar)
        .WithName("DesbloquearLineaCredito")
        .Produces<LineaCreditoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    public sealed record ActualizarLineaCreditoBody(decimal Limite, int PlazoDias, string? Clasificacion);
    public sealed record BloquearBody(string Motivo);
}
