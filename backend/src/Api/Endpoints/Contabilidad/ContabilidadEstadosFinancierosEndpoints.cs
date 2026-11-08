using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Contabilidad.Application.Reportes;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Contabilidad;

/// <summary>
/// F1-CON-08 · K10.10: balance general y estado de resultados bajo <c>/api/v1/contabilidad/reportes</c>. Solo lectura con el permiso
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

        return app;
    }
}
