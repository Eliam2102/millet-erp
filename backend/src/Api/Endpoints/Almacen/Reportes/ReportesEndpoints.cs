using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Almacen.Application.Reportes;
using Millet.Api.Auth;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Almacen.Reportes;

/// <summary>
/// Endpoints de reportes operativos (F8-PR1). Shape JSON canónico
/// (ADR-0036) que el FE renderiza con <c>&lt;ReporteShell&gt;</c>.
/// </summary>
public static class ReportesEndpoints
{
    public static IEndpointRouteBuilder MapReportesEndpoints(this IEndpointRouteBuilder app)
    {
        // ALFAK-HISTORIAL-ALMACEN
        app.MapGet("/api/v1/almacen/reportes/alfak-historial-almacen", async (
            [FromQuery] DateOnly desde,
            [FromQuery] DateOnly hasta,
            [FromQuery] Guid? subAlmacenId,
            [FromQuery] Guid? articuloId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new AlfakHistorialAlmacenQuery(desde, hasta, subAlmacenId, articuloId), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenReportesAlfak)
        .WithTags("Almacen")
        .WithName("ReporteAlfakHistorialAlmacen")
        .WithSummary("ALFAK-HISTORIAL-ALMACEN — movimientos del periodo agrupados por sub-almacén / artículo")
        .Produces<ReporteResponse<AlfakHistorialFila>>(StatusCodes.Status200OK);

        // SAP-REPORTE-EXISTENCIA-MP-CNK
        app.MapGet("/api/v1/almacen/reportes/mp-cnk", async (
            [FromQuery] Guid? subAlmacenId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new ExistenciaMpCnkQuery(subAlmacenId), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenReportesMpCnk)
        .WithTags("Almacen")
        .WithName("ReporteExistenciaMpCnk")
        .WithSummary("SAP-REPORTE-EXISTENCIA-MP-CNK — inventario diario de materiales directos no-vidrio")
        .Produces<ReporteResponse<ExistenciaMpCnkFila>>(StatusCodes.Status200OK);

        return app;
    }
}
