using MediatR;
using Microsoft.AspNetCore.Http;
using Millet.Api.Auth;
using Millet.CuentasPorCobrar.Application.CreditoDisponible;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorCobrar;

/// <summary>
/// Endpoint de crédito disponible por cliente (CXC-PR2, §11 del
/// 01-diseño). Devuelve el desglose por línea (moneda) con la bandera
/// <c>datoIncompleto</c> mientras `facturado` (CXC-PR3) y
/// `liberado_sin_factura` (gap G1) sigan provisionales.
/// </summary>
public static class CreditoDisponibleEndpoints
{
    public static IEndpointRouteBuilder MapCreditoDisponibleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/cuentas-por-cobrar/credito-disponible/{clienteId:guid}", async (
            Guid clienteId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new CreditoDisponibleQuery(clienteId), cancellationToken);
            return Results.Ok(response);
        })
        .WithTags("CuentasPorCobrar")
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarLineasCreditoLeer)
        .WithName("ObtenerCreditoDisponible")
        .Produces<CreditoDisponibleResponse>(StatusCodes.Status200OK);

        return app;
    }
}
