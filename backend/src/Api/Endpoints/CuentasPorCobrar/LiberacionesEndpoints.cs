using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorCobrar.Application.Autorizaciones;
using Millet.CuentasPorCobrar.Application.Common;
using Millet.CuentasPorCobrar.Application.Liberacion;
using Millet.CuentasPorCobrar.Domain.Liberacion;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorCobrar;

/// <summary>
/// Endpoints de decisión de liberación + overrides consumibles (CXC-PR4,
/// §11 del 01-diseño). La decisión es inmutable — correcciones = nueva
/// decisión. El write-back a A+W es CXC-PR9 (bloqueado por contrato).
/// </summary>
public static class LiberacionesEndpoints
{
    public static IEndpointRouteBuilder MapLiberacionesEndpoints(this IEndpointRouteBuilder app)
    {
        // -------- Decisiones --------
        var liberaciones = app
            .MapGroup("/api/v1/cuentas-por-cobrar/liberaciones")
            .WithTags("CuentasPorCobrar")
            .RequireAuthorization();

        liberaciones.MapGet("/", async (
            [FromQuery] string? pedidoRef,
            [FromQuery] Guid? clienteId,
            [FromQuery] ResultadoLiberacion? resultado,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarDecisionesLiberacionQuery(pedidoRef, clienteId, resultado, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarLiberacionDecidir)
        .WithName("ListarDecisionesLiberacion")
        .Produces<PagedResponse<DecisionLiberacionResponse>>(StatusCodes.Status200OK);

        liberaciones.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new ObtenerDecisionLiberacionQuery(id), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarLiberacionDecidir)
        .WithName("ObtenerDecisionLiberacion")
        .Produces<DecisionLiberacionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        liberaciones.MapPost("/", async (
            [FromBody] DecidirLiberacionCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-cobrar/liberaciones/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarLiberacionDecidir)
        .WithName("DecidirLiberacion")
        .Produces<DecisionLiberacionResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // -------- Autorizaciones (overrides consumibles) --------
        var autorizaciones = app
            .MapGroup("/api/v1/cuentas-por-cobrar/autorizaciones")
            .WithTags("CuentasPorCobrar")
            .RequireAuthorization();

        autorizaciones.MapGet("/", async (
            [FromQuery] EstadoAutorizacionCredito? estado,
            [FromQuery] Guid? beneficiarioUsuarioId,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarAutorizacionesCreditoQuery(estado, beneficiarioUsuarioId, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarLiberacionDecidir)
        .WithName("ListarAutorizacionesCredito")
        .Produces<PagedResponse<AutorizacionCreditoResponse>>(StatusCodes.Status200OK);

        autorizaciones.MapPost("/", async (
            [FromBody] CrearAutorizacionCreditoCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-cobrar/autorizaciones/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarLiberacionOverride)
        .WithName("CrearAutorizacionCredito")
        .Produces<AutorizacionCreditoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        autorizaciones.MapPost("/{id:guid}/cancelar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(new CancelarAutorizacionCreditoCommand(id, v), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarLiberacionOverride)
        .WithName("CancelarAutorizacionCredito")
        .Produces<AutorizacionCreditoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
