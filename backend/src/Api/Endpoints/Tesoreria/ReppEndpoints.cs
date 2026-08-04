using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Application.Repp;

namespace Millet.Api.Endpoints.Tesoreria;

/// <summary>
/// Endpoints del REPP recibido de proveedor (TES-PR8, §3.6.b / TES-4
/// sabor b): read model de pagos sin complemento con SLA de 5 días y
/// registro del REPP (UUID único + XML a blob) que publica
/// <c>tesoreria.repp-proveedor.recibido.v1</c> → CxP libera FALTA_REPP.
/// Ambos con permiso <c>tesoreria.repp.registrar</c> (§10 del diseño).
/// </summary>
public static class ReppEndpoints
{
    public static IEndpointRouteBuilder MapTesoreriaReppEndpoints(this IEndpointRouteBuilder app)
    {
        var permiso = PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaReppRegistrar;

        app.MapGet("/api/v1/tesoreria/repp-pendientes", async (
            [FromQuery] Guid? proveedorId,
            [FromQuery] bool? soloVencidos,
            [FromQuery] bool? incluirSinMetodo,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ReppPendientesQuery(
                    proveedorId, soloVencidos ?? false, incluirSinMetodo ?? true,
                    offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithTags("Tesoreria")
        .RequireAuthorization(permiso)
        .WithName("ReppPendientes")
        .Produces<PagedResponse<ReppPendienteResponse>>(StatusCodes.Status200OK);

        app.MapPost("/api/v1/tesoreria/repp-recibidos", async (
            [FromBody] RegistrarReppRecibidoCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/tesoreria/repp-recibidos/{response.Id}", response);
        })
        .WithTags("Tesoreria")
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(permiso)
        .WithName("RegistrarReppRecibido")
        .Produces<ReppRecibidoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
