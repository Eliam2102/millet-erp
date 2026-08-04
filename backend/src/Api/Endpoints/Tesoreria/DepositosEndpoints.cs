using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Application.Depositos;
using Millet.Tesoreria.Domain.Depositos;

namespace Millet.Api.Endpoints.Tesoreria;

/// <summary>
/// Endpoints de confirmación de depósitos de cliente (TES-PR7, §3.3 /
/// TES-9): bandeja de propuestas de CxC + expectativas de Caja, confirmar
/// (RN-6, publica <c>pago-cliente.confirmado.v1</c>) y rechazar (publica
/// <c>propuesta-aplicacion.rechazada.v1</c> [T-G7]). Confirmar ≠ timbrado:
/// el badge <c>reppTimbrado</c> cierra el ciclo fiscal aparte.
///
/// <para>
/// La bandeja usa el permiso <c>tesoreria.depositos.confirmar</c> (§6 del
/// levantamiento: Auxiliar y Jefe lo tienen; no existe un
/// <c>depositos.ver</c> separado en el seed de PR-1).
/// </para>
/// </summary>
public static class DepositosEndpoints
{
    public static IEndpointRouteBuilder MapTesoreriaDepositosEndpoints(this IEndpointRouteBuilder app)
    {
        var depositos = app
            .MapGroup("/api/v1/tesoreria/depositos")
            .WithTags("Tesoreria")
            .RequireAuthorization();

        depositos.MapGet("/", async (
            [FromQuery] EstadoDepositoConfirmacion? estado,
            [FromQuery] Guid? clienteId,
            [FromQuery] bool? soloPropuestas,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new DepositosQuery(estado, clienteId, soloPropuestas, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaDepositosConfirmar)
        .WithName("ListarDepositosConfirmacion")
        .Produces<PagedResponse<DepositoConfirmacionResponse>>(StatusCodes.Status200OK);

        depositos.MapPost("/{id:guid}/confirmar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] ConfirmarBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new ConfirmarDepositoCommand(id, body.MovimientoBancarioId, v), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaDepositosConfirmar)
        .WithName("ConfirmarDeposito")
        .Produces<DepositoConfirmacionResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        depositos.MapPost("/{id:guid}/rechazar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] RechazarBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new RechazarPropuestaDepositoCommand(id, body.Motivo, v), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaDepositosRechazar)
        .WithName("RechazarPropuestaDeposito")
        .Produces<DepositoConfirmacionResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    public sealed record ConfirmarBody(Guid MovimientoBancarioId);
    public sealed record RechazarBody(string Motivo);
}
