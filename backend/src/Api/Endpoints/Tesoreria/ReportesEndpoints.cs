using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Identidad.Domain;
using Millet.Tesoreria.Application.Reportes;
using Millet.Tesoreria.Application.Reportes.Comun;

namespace Millet.Api.Endpoints.Tesoreria;

/// <summary>
/// Endpoints de reportes operativos de Tesorería (TES-PR10, ADR-0036):
/// JSON estructurado que el frontend renderiza con
/// <c>&lt;ReporteShell&gt;</c> y exporta client-side.
/// </summary>
public static class ReportesEndpoints
{
    public static IEndpointRouteBuilder MapTesoreriaReportesEndpoints(this IEndpointRouteBuilder app)
    {
        var reportes = app
            .MapGroup("/api/v1/tesoreria/reportes")
            .WithTags("Tesoreria")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaReportesVer);

        reportes.MapGet("/flujo-efectivo", async (
            [FromQuery] DateOnly desde,
            [FromQuery] DateOnly hasta,
            [FromQuery] Guid? cuentaBancariaId,
            [FromQuery] string? moneda,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new FlujoEfectivoReporteQuery(desde, hasta, cuentaBancariaId, moneda),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithName("ReporteFlujoEfectivo")
        .Produces<ReporteJsonResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem();

        reportes.MapGet("/auxiliar-bancos", async (
            [FromQuery] Guid cuentaBancariaId,
            [FromQuery] DateOnly desde,
            [FromQuery] DateOnly hasta,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new AuxiliarBancosReporteQuery(cuentaBancariaId, desde, hasta),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithName("ReporteAuxiliarBancos")
        .Produces<ReporteJsonResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
