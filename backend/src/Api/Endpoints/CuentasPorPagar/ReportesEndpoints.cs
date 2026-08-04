using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.CuentasPorPagar.Application.Reportes.AntiguedadAnticipos;
using Millet.CuentasPorPagar.Application.Reportes.AntiguedadSaldos;
using Millet.CuentasPorPagar.Application.Reportes.CarteraPorCategoriaRevision;
using Millet.CuentasPorPagar.Application.Reportes.Comun;
using Millet.CuentasPorPagar.Application.Reportes.EstadosCuentaTcConsolidado;
using Millet.CuentasPorPagar.Application.Reportes.MovimientosTcPendientes;
using Millet.CuentasPorPagar.Application.Reportes.PasivosObras;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorPagar;

/// <summary>
/// Endpoints HTTP de reportes operativos del módulo CxP (F8-PR1,
/// ADR-0036). Todos devuelven <see cref="ReporteJsonResponse"/> con
/// el shape estandarizado (título, filtros, columnas, filas, totales).
/// El frontend los renderiza con <c>&lt;ReporteShell&gt;</c> y exporta
/// a PDF/Excel client-side.
/// </summary>
public static class ReportesEndpoints
{
    public static IEndpointRouteBuilder MapReportesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/cuentas-por-pagar/reportes")
            .WithTags("CuentasPorPagar")
            .RequireAuthorization();

        group.MapGet("/antiguedad-saldos", async (
            [FromQuery] DateOnly? fechaCorte,
            [FromQuery] Guid? proveedorId,
            [FromQuery] Guid? sucursalId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new AntiguedadSaldosProveedoresQuery(fechaCorte, proveedorId, sucursalId),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarReportesAntiguedad)
        .WithName("ReporteAntiguedadSaldos")
        .WithSummary("Antigüedad de saldos por proveedor — buckets 0-30 / 31-60 / 61-90 / +90 días")
        .Produces<ReporteJsonResponse>(StatusCodes.Status200OK);

        group.MapGet("/antiguedad-anticipos", async (
            [FromQuery] DateOnly? fechaCorte,
            [FromQuery] Guid? proveedorId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new AntiguedadAnticiposProveedoresQuery(fechaCorte, proveedorId),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarReportesAntiguedad)
        .WithName("ReporteAntiguedadAnticipos")
        .WithSummary("Antigüedad de anticipos por proveedor — entregado/amortizado/saldo amortizable")
        .Produces<ReporteJsonResponse>(StatusCodes.Status200OK);

        group.MapGet("/cartera", async (
            [FromQuery] DateOnly? fechaCorte,
            [FromQuery] Guid? proveedorId,
            [FromQuery] Guid? sucursalId,
            [FromQuery] bool? soloEnRevision,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new CarteraPorCategoriaRevisionQuery(fechaCorte, proveedorId, sucursalId, soloEnRevision),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarReportesCartera)
        .WithName("ReporteCartera")
        .WithSummary("Cartera de proveedores — cruz revisión × antigüedad")
        .Produces<ReporteJsonResponse>(StatusCodes.Status200OK);

        // ============ F8-PR2 ============

        group.MapGet("/movimientos-tc-pendientes", async (
            [FromQuery] Guid? tarjetaId,
            [FromQuery] Guid? usuarioQueUsoId,
            [FromQuery] DateOnly? fechaDesde,
            [FromQuery] DateOnly? fechaHasta,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new MovimientosTcPendientesConciliarQuery(tarjetaId, usuarioQueUsoId, fechaDesde, fechaHasta),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarReportesTc)
        .WithName("ReporteMovimientosTcPendientes")
        .WithSummary("Movimientos de TC en Registrado sin conciliar con estado de cuenta")
        .Produces<ReporteJsonResponse>(StatusCodes.Status200OK);

        group.MapGet("/estados-cuenta-tc-consolidado", async (
            [FromQuery] Guid? tarjetaId,
            [FromQuery] EstadoCuentaTcStatus? estado,
            [FromQuery] DateOnly? periodoDesde,
            [FromQuery] DateOnly? periodoHasta,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new EstadosCuentaTcConsolidadoQuery(tarjetaId, estado, periodoDesde, periodoHasta),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarReportesTc)
        .WithName("ReporteEstadosCuentaTcConsolidado")
        .WithSummary("Estados de cuenta TC con totales matched/sugerencias/sin-match + factura banco")
        .Produces<ReporteJsonResponse>(StatusCodes.Status200OK);

        group.MapGet("/pasivos-obras", async (
            [FromQuery] Guid sucursalId,
            [FromQuery] DateOnly? fechaCorte,
            [FromQuery] Guid? proveedorId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (sucursalId == Guid.Empty)
            {
                return Results.Problem(
                    title: "sucursalId requerido",
                    detail: "El parámetro sucursalId es obligatorio (identifica la obra/sucursal).",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var response = await mediator.Send(
                new PasivosObrasQuery(sucursalId, fechaCorte, proveedorId),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarReportesCartera)
        .WithName("ReportePasivosObras")
        .WithSummary("Pasivos por sucursal/obra — para integración con módulo Obras futuro")
        .Produces<ReporteJsonResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }
}
