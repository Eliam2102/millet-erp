using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Compras.Application.Reportes.AnalisisProveedores;
using Millet.Compras.Domain.Oc;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Compras.Reportes;

/// <summary>
/// F1-COM-13 (86e3jvu98): análisis de compras por periodo, saldos pendientes
/// por OC y cumplimiento de proveedores, con detalle de las órdenes que
/// sustentan cada total. Sólo lectura; alcance por sucursal (ADR-0051).
/// </summary>
public static class AnalisisProveedoresEndpoints
{
    public static IEndpointRouteBuilder MapAnalisisProveedoresEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/compras/reportes/analisis-proveedores")
            .WithTags("Compras")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesReportesAnalisisProveedores);

        group.MapGet("/compras", async (IMediator mediator, [AsParameters] FiltrosRequest filtros,
                [FromQuery] AgruparComprasPor? agruparPor, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ConsultarComprasPeriodoQuery(filtros.ToFiltros(), agruparPor ?? AgruparComprasPor.Proveedor), ct)))
            .WithName("AnalisisProveedoresCompras")
            .WithSummary("Compras por periodo agrupadas por proveedor, artículo, departamento o mes (F1-COM-13)")
            .Produces<AnalisisProveedoresResponse>()
            .ProducesValidationProblem();

        group.MapGet("/saldos", async (IMediator mediator, [AsParameters] FiltrosRequest filtros, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ConsultarSaldosOcQuery(filtros.ToFiltros()), ct)))
            .WithName("AnalisisProveedoresSaldos")
            .WithSummary("Saldos pendientes por OC (recibir, facturar, pagar) y saldo inicial (F1-COM-13)")
            .Produces<AnalisisProveedoresResponse>()
            .ProducesValidationProblem();

        group.MapGet("/cumplimiento", async (IMediator mediator, [AsParameters] FiltrosRequest filtros, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ConsultarCumplimientoProveedoresQuery(filtros.ToFiltros()), ct)))
            .WithName("AnalisisProveedoresCumplimiento")
            .WithSummary("Cumplimiento por proveedor: a tiempo, completo, incidencias y diferencias de precio (F1-COM-13)")
            .Produces<AnalisisProveedoresResponse>()
            .ProducesValidationProblem();

        group.MapGet("/ordenes", async (IMediator mediator, [AsParameters] FiltrosRequest filtros,
                [FromQuery] string? indicador, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ConsultarOrdenesAnalisisQuery(filtros.ToFiltros(), ParseIndicador(indicador)), ct)))
            .WithName("AnalisisProveedoresOrdenes")
            .WithSummary("Órdenes que sustentan un total del análisis, con sus líneas y facturas (F1-COM-13)")
            .Produces<OrdenesAnalisisResponse>()
            .ProducesValidationProblem();

        return app;
    }

    private static IndicadorDetalle ParseIndicador(string? indicador) => indicador switch
    {
        null or "" or "compras" => IndicadorDetalle.Compras,
        "saldos" => IndicadorDetalle.Saldos,
        "a-tiempo" => IndicadorDetalle.ATiempo,
        "completo" => IndicadorDetalle.Completo,
        "incidencias" => IndicadorDetalle.Incidencias,
        "diferencias-precio" => IndicadorDetalle.DiferenciasPrecio,
        _ => throw new BadHttpRequestException($"Indicador desconocido: {indicador}."),
    };

    public sealed record FiltrosRequest(
        [FromQuery] DateOnly? Desde,
        [FromQuery] DateOnly? Hasta,
        [FromQuery] Guid? SucursalId,
        [FromQuery] Guid? ProveedorId,
        [FromQuery] Guid? ArticuloId,
        [FromQuery] Guid? DepartamentoId,
        [FromQuery] string? Moneda,
        [FromQuery] EstadoOrdenCompra? Estado)
    {
        // Fechas ausentes llegan como default y las rechaza el validador (400).
        public FiltrosAnalisis ToFiltros() => new(Desde ?? default, Hasta ?? default, SucursalId, ProveedorId,
            ArticuloId, DepartamentoId, Moneda, Estado);
    }
}
