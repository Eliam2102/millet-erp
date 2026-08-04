using MediatR;
using Microsoft.AspNetCore.Http;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Application.Vales;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Almacen.Vales;

/// <summary>
/// Endpoints de vales urgentes (F5-PR1, Variante B). Bajo
/// <c>/api/v1/almacen/salidas/vale</c> y
/// <c>/api/v1/almacen/salidas/{id}/regularizar</c>.
/// </summary>
public static class ValesEndpoints
{
    public static IEndpointRouteBuilder MapValesEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /api/v1/almacen/salidas/vale
        app.MapPost("/api/v1/almacen/salidas/vale", async (
            RegistrarSalidaPorValeCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created(
                $"/api/v1/almacen/salidas/{response.SalidaId}",
                response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasPorVale)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithTags("Almacen")
        .WithName("RegistrarSalidaPorVale")
        .WithSummary("Registrar salida urgente con vale firmado (A14, regulariza en 48h)")
        .Produces<RegistrarSalidaResponse>(StatusCodes.Status201Created);

        // POST /api/v1/almacen/salidas/{id}/regularizar
        app.MapPost("/api/v1/almacen/salidas/{id:guid}/regularizar", async (
            Guid id,
            RegularizarValeRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await mediator.Send(new RegularizarSalidaPorValeCommand(id, body.RqRegularizadoraId), ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasPorVale)
        .WithTags("Almacen")
        .WithName("RegularizarVale")
        .WithSummary("Vincular RQ posterior al vale (regularización A14)")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}

public sealed record RegularizarValeRequest(Guid RqRegularizadoraId);
