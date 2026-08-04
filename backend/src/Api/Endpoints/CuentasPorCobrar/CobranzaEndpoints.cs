using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorCobrar.Application.Cobranza;
using Millet.CuentasPorCobrar.Application.Common;
using Millet.CuentasPorCobrar.Domain.Cobranza;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorCobrar;

/// <summary>
/// Endpoints de seguimiento de cobranza (CXC-PR5, §11 del 01-diseño).
/// Append-only: no hay edición ni borrado — cada gestión deja su rastro.
/// Lectura con <c>cartera.leer</c> (ADR-0041); registro con
/// <c>cobranza.registrar</c>.
/// </summary>
public static class CobranzaEndpoints
{
    public static IEndpointRouteBuilder MapCobranzaEndpoints(this IEndpointRouteBuilder app)
    {
        var cobranza = app
            .MapGroup("/api/v1/cuentas-por-cobrar/cobranza")
            .WithTags("CuentasPorCobrar")
            .RequireAuthorization();

        cobranza.MapGet("/", async (
            [FromQuery] Guid clienteId,
            [FromQuery] ResultadoCobranza? resultado,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarSeguimientosCobranzaQuery(clienteId, resultado, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCarteraLeer)
        .WithName("ListarSeguimientosCobranza")
        .Produces<PagedResponse<SeguimientoCobranzaResponse>>(StatusCodes.Status200OK);

        cobranza.MapPost("/", async (
            [FromBody] RegistrarSeguimientoCobranzaCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-cobrar/cobranza/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCobranzaRegistrar)
        .WithName("RegistrarSeguimientoCobranza")
        .Produces<SeguimientoCobranzaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
