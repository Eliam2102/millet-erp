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

        group.MapGet("/dependencias-revisoras", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarDependenciasRevisorasQuery(), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarFacturasLeer)
            .WithName("ListarDependenciasRevisoras");

        // P8.6: lo usan facturas, tarjeta, caja chica y viáticos (al leer o al capturar), cada una con su permiso: basta cualquiera.
        group.MapGet("/clasificaciones-no-deducibles", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new Millet.CuentasPorPagar.Application.Catalogos.ClasificacionesNoDeducibles.ListarClasificacionesNoDeduciblesQuery(), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + string.Join('|',
                PermisosCanonicos.CuentasPorPagarFacturasLeer,
                PermisosCanonicos.CuentasPorPagarTcLeer, PermisosCanonicos.CuentasPorPagarTcRegistrarMovimiento,
                PermisosCanonicos.CuentasPorPagarComprobacionesLeer, PermisosCanonicos.CuentasPorPagarComprobacionesCapturar,
                PermisosCanonicos.CuentasPorPagarViaticosLeer, PermisosCanonicos.CuentasPorPagarViaticosCapturarComprobacion))
            .WithName("ListarClasificacionesNoDeducibles");
        return app;
    }
}
