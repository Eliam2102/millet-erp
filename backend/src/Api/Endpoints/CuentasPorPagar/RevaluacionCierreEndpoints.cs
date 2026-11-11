using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorPagar.Application.Reportes.Comun;
using Millet.CuentasPorPagar.Application.Revaluacion;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorPagar;

/// <summary>CXP-12: revaluación de cierre de saldos por pagar en moneda extranjera y su reporte por proveedor y documento.</summary>
public static class RevaluacionCierreEndpoints
{
    public sealed record EjecutarRevaluacionBody(int Anio, int Mes);

    public static IEndpointRouteBuilder MapRevaluacionCierreEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/cuentas-por-pagar/revaluaciones").WithTags("CuentasPorPagar").RequireAuthorization();

        group.MapGet("/", async (IMediator mediator, CancellationToken ct) => Results.Ok(await mediator.Send(new ListarRevaluacionesCierreQuery(), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarRevaluacionConsultar)
            .WithName("ListarRevaluacionesCierre")
            .WithSummary("Revaluaciones de cierre ya corridas, la más reciente primero")
            .Produces<IReadOnlyList<RevaluacionCierreListItem>>(StatusCodes.Status200OK);

        group.MapPost("/", async ([FromBody] EjecutarRevaluacionBody body, IMediator mediator, CancellationToken ct) =>
                Results.Created($"/api/v1/cuentas-por-pagar/revaluaciones/{body.Anio}/{body.Mes}",
                    await mediator.Send(new EjecutarRevaluacionCierreCommand(body.Anio, body.Mes), ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarRevaluacionEjecutar)
            .WithName("EjecutarRevaluacionCierre")
            .WithSummary("Revalúa al cierre del mes los saldos abiertos en moneda extranjera (TC del último día)")
            .WithDescription("Publica un evento por proveedor para la póliza de revaluación (no realizada); la reversa del día 1 sale sola. Idempotency-Key obligatorio.")
            .Produces<RevaluacionCierreResumen>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{anio:int}/{mes:int}/vista-previa", async (int anio, int mes, IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new VistaPreviaRevaluacionCierreQuery(anio, mes), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarRevaluacionEjecutar)
            .WithName("VistaPreviaRevaluacionCierre")
            .WithSummary("Vista previa (M-317): cálculo y pólizas por proveedor y sucursal sin registrar la revaluación ni publicar eventos")
            .Produces<VistaPreviaRevaluacionResponse>(StatusCodes.Status200OK);

        group.MapGet("/{anio:int}/{mes:int}/reporte", async (int anio, int mes, IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new ReporteRevaluacionCierreQuery(anio, mes), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarRevaluacionConsultar)
            .WithName("ReporteRevaluacionCierre")
            .WithSummary("Reporte de la revaluación de un mes por proveedor y documento")
            .Produces<ReporteJsonResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
