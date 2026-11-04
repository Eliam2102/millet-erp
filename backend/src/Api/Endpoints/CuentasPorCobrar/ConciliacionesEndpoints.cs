using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorCobrar.Application.Conciliacion;
using Millet.CuentasPorCobrar.Domain.Conciliacion;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorCobrar;

/// <summary>
/// C7.7 · conciliación diaria de cartera contra Facturación. Leer las corridas pide
/// el permiso de cartera; conciliar a demanda pide su propio permiso (ADR-0041).
/// </summary>
public static class ConciliacionesEndpoints
{
    public sealed record ConciliarCarteraRequest(DateOnly Fecha);

    public static IEndpointRouteBuilder MapConciliacionesEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app
            .MapGroup("/api/v1/cuentas-por-cobrar/conciliaciones")
            .WithTags("CuentasPorCobrar")
            .RequireAuthorization();

        grupo.MapGet("", async (
            [FromQuery] DateOnly? desde,
            [FromQuery] DateOnly? hasta,
            IMediator mediator,
            CancellationToken cancellationToken) =>
            Results.Ok(await mediator.Send(new ListarConciliacionesQuery(desde, hasta), cancellationToken)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCarteraLeer)
        .WithName("ListarConciliacionesCartera")
        .Produces<IReadOnlyList<ConciliacionResumenResponse>>(StatusCodes.Status200OK);

        grupo.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
            Results.Ok(await mediator.Send(new ConciliacionDetalleQuery(id), cancellationToken)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCarteraLeer)
        .WithName("ObtenerConciliacionCartera")
        .Produces<ConciliacionDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        grupo.MapPost("", async (
            ConciliarCarteraRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var corridas = await mediator.Send(
                new ConciliarCarteraCommand(request.Fecha, OrigenConciliacion.Demanda), cancellationToken);
            var corrida = corridas.Single();
            return Results.Created($"/api/v1/cuentas-por-cobrar/conciliaciones/{corrida.Id}", corrida);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarConciliacionEjecutar)
        .WithName("ConciliarCartera")
        .Produces<ConciliacionResumenResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
