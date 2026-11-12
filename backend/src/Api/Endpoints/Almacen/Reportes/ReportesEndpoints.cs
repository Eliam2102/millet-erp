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

        // F1-ALM-13-a: valuación al cierre contra el saldo de la cuenta de inventario. Mismo permiso que el ALFAK (es su total).
        app.MapGet("/api/v1/almacen/reportes/cuadre-inventario", async (
            [FromQuery] int anio,
            [FromQuery] int mes,
            IMediator mediator,
            CancellationToken ct) => Results.Ok(await mediator.Send(new CuadreInventarioContableQuery(anio, mes), ct)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenReportesAlfak)
        .WithTags("Almacen")
        .WithName("ReporteCuadreInventarioContable")
        .WithSummary("Cuadre del cierre: valuación del ALFAK contra el saldo de la cuenta de inventario por sucursal")
        .Produces<ReporteResponse<CuadreInventarioFila>>(StatusCodes.Status200OK);

        // F1-MP-09: trazabilidad de un artículo (y lote): compras, consumos, merma y saldo. Mismo permiso que el ALFAK (es su detalle).
        app.MapGet("/api/v1/almacen/reportes/trazabilidad-mp", async (
            [FromQuery] Guid articuloId,
            [FromQuery] DateOnly desde,
            [FromQuery] DateOnly hasta,
            [FromQuery] string? lote,
            [FromQuery] Guid? subAlmacenId,
            IMediator mediator,
            CancellationToken ct) => Results.Ok(await mediator.Send(new TrazabilidadMpQuery(articuloId, desde, hasta, lote, subAlmacenId), ct)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenReportesAlfak)
        .WithTags("Almacen")
        .WithName("ReporteTrazabilidadMp")
        .WithSummary("TRAZABILIDAD-MP — compra, lote, consumo, merma y saldo de un artículo")
        .Produces<ReporteResponse<TrazabilidadMpFila>>(StatusCodes.Status200OK);

        // F1-ALM-15: kardex de un artículo por almacén, ubicación y periodo. Mismo permiso y alcance por sucursal que el ALFAK.
        app.MapGet("/api/v1/almacen/reportes/kardex", async (
            [FromQuery] Guid articuloId,
            [FromQuery] DateOnly desde,
            [FromQuery] DateOnly hasta,
            [FromQuery] Guid? subAlmacenId,
            [FromQuery] Guid? ubicacionId,
            IMediator mediator,
            CancellationToken ct) => Results.Ok(await mediator.Send(new KardexQuery(articuloId, desde, hasta, subAlmacenId, ubicacionId), ct)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenReportesAlfak)
        .WithTags("Almacen")
        .WithName("ReporteKardex")
        .WithSummary("KARDEX-ALMACEN — movimientos de un artículo con saldo, costo y valor después de cada uno")
        .Produces<ReporteResponse<KardexFila>>(StatusCodes.Status200OK);

        // M-339: consumo de insumos por línea (salidas por consumo/vale a costo). Mismo permiso y alcance por sucursal que el ALFAK.
        app.MapGet("/api/v1/almacen/reportes/consumo-insumos-linea", async (
            [FromQuery] DateOnly desde,
            [FromQuery] DateOnly hasta,
            [FromQuery] Guid? sucursalId,
            [FromQuery] Guid? centroCostoId,
            [FromQuery] Guid? articuloId,
            IMediator mediator,
            CancellationToken ct) => Results.Ok(await mediator.Send(new ConsumoInsumosLineaQuery(desde, hasta, sucursalId, centroCostoId, articuloId), ct)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenReportesAlfak)
        .WithTags("Almacen")
        .WithName("ReporteConsumoInsumosLinea")
        .WithSummary("CONSUMO-INSUMOS-LINEA — salidas por consumo agrupadas por línea y artículo, a costo")
        .Produces<ReporteResponse<ConsumoInsumosLineaFila>>(StatusCodes.Status200OK);

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
