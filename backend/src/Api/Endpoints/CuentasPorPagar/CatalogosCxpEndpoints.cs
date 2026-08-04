using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.CuentasPorPagar.Application.Catalogos;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorPagar;

/// <summary>
/// Endpoints de catálogos read-only del módulo CxP (F4-PR1). Por
/// ahora solo motivos de revisión; cuando entren NotaCargo y TC, se
/// agregan aquí también.
/// </summary>
public static class CatalogosCxpEndpoints
{
    public static IEndpointRouteBuilder MapCatalogosCxpEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/cuentas-por-pagar/catalogos")
            .WithTags("CuentasPorPagar")
            .RequireAuthorization();

        group.MapGet("/motivos-revision", async (
            [FromQuery] bool? incluirInactivos,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarMotivosRevisionQuery(incluirInactivos ?? false),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarFacturasLeer)
        .WithName("ListarMotivosRevision")
        .WithSummary("Catálogo de motivos de revisión (con SLA por motivo)")
        .Produces<IReadOnlyList<MotivoRevisionResponse>>(StatusCodes.Status200OK);

        return app;
    }
}
