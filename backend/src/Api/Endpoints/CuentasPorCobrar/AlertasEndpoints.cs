using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorCobrar.Application.Alertas;
using Millet.CuentasPorCobrar.Application.Common;
using Millet.CuentasPorCobrar.Domain.Alertas;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorCobrar;

/// <summary>
/// Endpoints de alertas de cartera (CXC-PR8, §11 del 01-diseño).
/// Lectura con <c>cartera.leer</c> (cuya descripción incluye alertas);
/// atender es una gestión de cartera → <c>cobranza.registrar</c>.
/// </summary>
public static class AlertasEndpoints
{
    public static IEndpointRouteBuilder MapAlertasEndpoints(this IEndpointRouteBuilder app)
    {
        var alertas = app
            .MapGroup("/api/v1/cuentas-por-cobrar/alertas")
            .WithTags("CuentasPorCobrar")
            .RequireAuthorization();

        alertas.MapGet("/", async (
            [FromQuery] bool? atendida,
            [FromQuery] Guid? clienteId,
            [FromQuery] TipoAlertaCartera? tipo,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarAlertasCarteraQuery(atendida, clienteId, tipo, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCarteraLeer)
        .WithName("ListarAlertasCartera")
        .Produces<PagedResponse<AlertaCarteraResponse>>(StatusCodes.Status200OK);

        alertas.MapPost("/{id:guid}/atender", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(new AtenderAlertaCarteraCommand(id, v), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCobranzaRegistrar)
        .WithName("AtenderAlertaCartera")
        .Produces<AlertaCarteraResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
