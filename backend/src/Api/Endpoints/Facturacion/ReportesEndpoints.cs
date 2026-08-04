using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Facturacion.Application.Reportes;
using Millet.Facturacion.Application.Reportes.CfdisPorObra;
using Millet.Facturacion.Application.Reportes.EstadosFacturasAnticipo;
using Millet.Facturacion.Application.Reportes.LiquidacionCaja;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>
/// Reportes del módulo Facturación (F11): liquidación de caja, estados de
/// facturas de anticipo, CFDIs por obra. Contrato JSON ADR-0036; export
/// client-side. <c>/api/v1/facturacion/reportes</c>.
/// </summary>
public static class ReportesEndpoints
{
    public static IEndpointRouteBuilder MapFacturacionReportesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/facturacion/reportes")
            .WithTags("Facturacion");

        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionReportesLeer;

        // GET: liquidación de caja (permiso de caja).
        group.MapGet("/liquidacion-caja", async (
            [FromQuery] Guid? sucursalId,
            [FromQuery] DateTimeOffset desde,
            [FromQuery] DateTimeOffset hasta,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var r = await mediator.Send(new LiquidacionCajaQuery(sucursalId, desde, hasta), ct);
            return Results.Ok(r);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCajaLiquidar)
        .WithName("LiquidacionCaja")
        .WithSummary("Reporte de liquidación de caja (facturado por forma de pago)")
        .Produces<ReporteResponse<LiquidacionCajaFila>>(StatusCodes.Status200OK);

        // GET: estados de facturas de anticipo.
        group.MapGet("/estados-anticipos", async (
            [FromQuery] Guid? clienteId,
            [FromQuery] EstadoAnticipo? estado,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var r = await mediator.Send(new EstadosFacturasAnticipoQuery(clienteId, estado), ct);
            return Results.Ok(r);
        })
        .RequireAuthorization(leer)
        .WithName("EstadosFacturasAnticipo")
        .WithSummary("Reporte de estados de facturas de anticipo")
        .Produces<ReporteResponse<EstadoFacturaAnticipoFila>>(StatusCodes.Status200OK);

        // GET: CFDIs por obra.
        group.MapGet("/cfdis-por-obra/{obraId:long}", async (
            long obraId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var r = await mediator.Send(new CfdisPorObraQuery(obraId), ct);
            return Results.Ok(r);
        })
        .RequireAuthorization(leer)
        .WithName("CfdisPorObra")
        .WithSummary("Reporte de CFDIs vinculados a una obra")
        .Produces<ReporteResponse<CfdiObraFila>>(StatusCodes.Status200OK);

        return app;
    }
}
