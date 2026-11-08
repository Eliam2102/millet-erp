using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Contabilidad.Application.Reportes;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Contabilidad;

/// <summary>
/// F1-CON-08 · K10.10: balance general, estado de resultados e integraciones del auditor bajo <c>/api/v1/contabilidad/reportes</c>. Solo lectura con el permiso
/// de lectura de pólizas, igual que la balanza (no se crean permisos). La sucursal es un filtro opcional; sin ella, consolidado.
/// </summary>
public static class ContabilidadEstadosFinancierosEndpoints
{
    public static IEndpointRouteBuilder MapContabilidadEstadosFinancierosEndpoints(this IEndpointRouteBuilder app)
    {
        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPolizasLeer;
        var g = app.MapGroup("/api/v1/contabilidad/reportes").WithTags("Contabilidad · Reportes").RequireAuthorization();

        g.MapGet("/balance-general", async (
            [FromQuery] string? anio, [FromQuery] string? periodo, [FromQuery] string? comparar, [FromQuery] string? sucursalId,
            IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                FiltrosEstadoFinanciero.Leer(EstadoFinanciero.BalanceGeneral, anio, periodo, null, comparar, sucursalId), ct)))
        .RequireAuthorization(leer).WithName("BalanceGeneral")
        .WithSummary("Balance general al cierre del periodo, por rubros del catálogo, con comparativo opcional del año anterior")
        .WithDescription("Saldos de todo el historial hasta el periodo. El capital incluye el resultado del ejercicio y los resultados de ejercicios anteriores sin cierre anual. Las cuentas sin rubro salen en «Sin clasificar» y en avisos.")
        .Produces<EstadoFinancieroResponse>().ProducesValidationProblem();

        g.MapGet("/estado-resultados", async (
            [FromQuery] string? anio, [FromQuery] string? periodo, [FromQuery] string? acumulado, [FromQuery] string? comparar,
            [FromQuery] string? sucursalId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                FiltrosEstadoFinanciero.Leer(EstadoFinanciero.EstadoResultados, anio, periodo, acumulado, comparar, sucursalId), ct)))
        .RequireAuthorization(leer).WithName("EstadoResultados")
        .WithSummary("Estado de resultados del periodo o acumulado del ejercicio, por rubros, con comparativo opcional del año anterior")
        .Produces<EstadoFinancieroResponse>().ProducesValidationProblem();

        g.MapGet("/integracion", async (
            [FromQuery] string? tipo, [FromQuery] string? anio, [FromQuery] string? desde, [FromQuery] string? hasta,
            [FromQuery] string? sucursalId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(FiltrosIntegracion.Leer(tipo, anio, desde, hasta, sucursalId), ct)))
        .RequireAuthorization(leer).WithName("IntegracionCuentasAuditor")
        .WithSummary("Integración del auditor por cuenta y cliente o proveedor: ingresos, costo, gastos, financiamiento o compras")
        .WithDescription("tipo = Ingresos | Costo | Gastos | Financiamiento | Compras. Las cuatro primeras siguen los rubros del estado de resultados; compras = movimientos de las cuentas colectivas de proveedores. Una partida sin tercero toma el de su póliza.")
        .Produces<IntegracionResponse>().ProducesValidationProblem();

        return app;
    }
}
