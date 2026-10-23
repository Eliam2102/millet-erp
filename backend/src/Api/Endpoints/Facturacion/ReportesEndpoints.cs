using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Facturacion.Application.Reportes;
using Millet.Facturacion.Application.Reportes.CierreDia;
using Millet.Facturacion.Application.Reportes.CfdisPorObra;
using Millet.Facturacion.Application.Reportes.ComparativaAnticipos;
using Millet.Facturacion.Application.Reportes.EstadosFacturasAnticipo;
using Millet.Facturacion.Application.Reportes.LiquidacionCaja;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>
/// Reportes del módulo Facturación (F11): liquidación de caja, estados de
/// facturas de anticipo, comparativa de anticipos vs aplicaciones (F6.2),
/// CFDIs por obra. Contrato JSON ADR-0036; export
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

        // GET: comparativa de anticipos vs aplicaciones y relación CFDI (F6.2).
        group.MapGet("/comparativa-anticipos", async (
            [FromQuery] Guid? sucursalId,
            [FromQuery] Guid? clienteId,
            [FromQuery] DateTimeOffset? desde,
            [FromQuery] DateTimeOffset? hasta,
            [FromQuery] DateTimeOffset? corte,
            [FromQuery] EstadoComparativaAnticipo? estado,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var r = await mediator.Send(
                new ComparativaAnticiposQuery(sucursalId, clienteId, desde, hasta, corte, estado), ct);
            return Results.Ok(r);
        })
        .RequireAuthorization(leer)
        .WithName("ComparativaAnticipos")
        .WithSummary("Comparativa de anticipos contra aplicaciones (egresos 07) con saldo a fecha de corte")
        .Produces<ReporteResponse<ComparativaAnticipoFila>>(StatusCodes.Status200OK)
        .ProducesValidationProblem();

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

        // F6.7: cierre del día por sucursal — monitor de estatus de CFDI y
        // conciliación facturado contra ingresado. Alcance por sucursal
        // (ADR-0051) en el behavior: asociado a la sucursal o bypass
        // `facturacion.reportes.leer-todas-sucursales`.
        group.MapGet("/monitor-cfdi", async (
            [FromQuery] Guid sucursalId,
            [FromQuery] DateOnly dia,
            [FromQuery] Guid? cajeroId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var r = await mediator.Send(new MonitorCfdiQuery(sucursalId, dia, cajeroId), ct);
            return Results.Ok(r);
        })
        .RequireAuthorization(leer)
        .WithName("MonitorCfdi")
        .WithSummary("Monitor de estatus de CFDI por sucursal y día (timbrados, cancelados, sin timbrar y estatus SAT)")
        .Produces<MonitorCfdiResponse>(StatusCodes.Status200OK);

        group.MapPost("/monitor-cfdi/verificar-sat", async (
            [FromQuery] Guid sucursalId,
            [FromQuery] DateOnly dia,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var r = await mediator.Send(new VerificarEstatusSatCommand(sucursalId, dia), ct);
            return Results.Ok(r);
        })
        .RequireAuthorization(leer)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("VerificarEstatusSat")
        .WithSummary("Consulta al SAT el estatus de los CFDI timbrados del día y lo guarda")
        .Produces<VerificarEstatusSatResponse>(StatusCodes.Status200OK);

        group.MapGet("/conciliacion-facturado-ingresado", async (
            [FromQuery] Guid sucursalId,
            [FromQuery] DateOnly dia,
            [FromQuery] Guid? cajeroId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var r = await mediator.Send(new ConciliacionFacturadoIngresadoQuery(sucursalId, dia, cajeroId), ct);
            return Results.Ok(r);
        })
        .RequireAuthorization(leer)
        .WithName("ConciliacionFacturadoIngresado")
        .WithSummary("Conciliación diaria facturado contra ingresado por sucursal y cajero")
        .Produces<ConciliacionFacturadoIngresadoResponse>(StatusCodes.Status200OK);

        return app;
    }
}
