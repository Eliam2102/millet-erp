using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.DatosMaestros.Application.Clientes;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.Endpoints.DatosMaestros;

public sealed record UnificarRfcRepetidoRequest(string? Rfc, Guid PrincipalClienteId, string? Motivo);

/// <summary>
/// Clientes de A+W que comparten RFC (86e3qcgyn): ver los grupos con su historial y unificarlos en un principal.
/// Solo toca el ERP; nunca escribe en A+W. Doc integration/05 §10.8.
/// </summary>
public static class RfcRepetidosEndpoints
{
    private const string Base = "/api/v1/datos-maestros/clientes/rfc-repetidos";

    public static IEndpointRouteBuilder MapRfcRepetidosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Base).WithTags("DatosMaestros");

        group.MapGet("", async (
            [FromQuery] int? offset, [FromQuery] int? limit, [FromQuery] bool? soloPendientes,
            IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarRfcRepetidosQuery(offset ?? 0, limit ?? 25, soloPendientes ?? true), ct)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosClientesSincronizar)
        .WithName("ListarRfcRepetidosClientes")
        .WithSummary("Grupos de clientes que comparten RFC, con historial de pedidos y principal sugerido")
        .Produces<ListarRfcRepetidosResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/unificar", async (
            [FromBody] UnificarRfcRepetidoRequest body, IMediator mediator, ICurrentUserContext user, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new UnificarRfcRepetidoCommand(
                body.Rfc ?? string.Empty, body.PrincipalClienteId, body.Motivo ?? string.Empty,
                user.UserName ?? user.Email ?? user.UserId?.ToString() ?? "desconocido"), ct)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosClientesUnificar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("UnificarRfcRepetidoClientes")
        .WithSummary("Unifica los clientes de un RFC repetido en el principal elegido (Fusionar o Agrupar según historial)")
        .Produces<RfcRepetidoGrupoDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }
}
