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
/// Bandeja «Diferencias de corte» (86e3vr1tj, Millet 23-oct): Tesorería autoriza o rechaza el
/// faltante/sobrante del corte de caja. Un solo permiso (<c>tesoreria.diferencias-corte.autorizar</c>)
/// gobierna ver y resolver; el cajero del corte no puede resolver la suya (regla de dominio, 422).
/// </summary>
public static class DiferenciasCorteEndpoints
{
    public sealed record RechazarDiferenciaBody(string Motivo);

    public static IEndpointRouteBuilder MapTesoreriaDiferenciasCorteEndpoints(this IEndpointRouteBuilder app)
    {
        var permiso = PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaDiferenciasCorteAutorizar;
        var grupo = app
            .MapGroup("/api/v1/tesoreria/diferencias-corte")
            .WithDocumentoSucursalScope("diferencia_corte", "tesoreria.documentos", "id")
            .WithTags("Tesoreria")
            .RequireAuthorization();

        grupo.MapGet("/", async (
            [FromQuery] EstadoDiferenciaCorte? estado, [FromQuery] int? offset, [FromQuery] int? limit,
            IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new DiferenciasCorteQuery(estado, offset ?? 0, limit ?? 50), ct)))
        .RequireAuthorization(permiso)
        .WithName("ListarDiferenciasCorte")
        .Produces<PagedResponse<DiferenciaCorteResponse>>(StatusCodes.Status200OK);

        grupo.MapPost("/{id:guid}/autorizar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator, CancellationToken ct) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);
            return Results.Ok(await mediator.Send(new AutorizarDiferenciaCorteCommand(id, v), ct));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(permiso)
        .WithName("AutorizarDiferenciaCorte")
        .Produces<DiferenciaCorteResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        grupo.MapPost("/{id:guid}/rechazar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] RechazarDiferenciaBody body,
            IMediator mediator, CancellationToken ct) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);
            return Results.Ok(await mediator.Send(new RechazarDiferenciaCorteCommand(id, body.Motivo, v), ct));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(permiso)
        .WithName("RechazarDiferenciaCorte")
        .Produces<DiferenciaCorteResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
