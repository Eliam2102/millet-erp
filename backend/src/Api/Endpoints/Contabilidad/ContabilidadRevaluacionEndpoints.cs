using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Contabilidad.Application.Revaluacion;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Contabilidad;

/// <summary>
/// CON-11 (86e3kdypd): revaluación de cierre de bancos y clientes en moneda extranjera bajo <c>/api/v1/contabilidad/revaluacion-cierre</c>.
/// Consulta con el permiso de lectura de pólizas; ejecutar y revertir con el de captura (no se crean permisos, igual que K10.7).
/// Proveedores: <c>/api/v1/cuentas-por-pagar/revaluacion-cierre</c> (CXP-12).
/// </summary>
public static class ContabilidadRevaluacionEndpoints
{
    public static IEndpointRouteBuilder MapContabilidadRevaluacionEndpoints(this IEndpointRouteBuilder app)
    {
        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPolizasLeer;
        var capturar = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPolizasCapturar;
        var g = app.MapGroup("/api/v1/contabilidad/revaluacion-cierre").WithTags("Contabilidad · Revaluación de cierre").RequireAuthorization();

        g.MapGet("/", async ([FromQuery] int anio, [FromQuery] int mes, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ConsultarRevaluacionCierreQuery(anio, mes), ct)))
        .RequireAuthorization(leer).WithName("ConsultarRevaluacionCierreContable")
        .WithSummary("Vista previa de la revaluación del mes (bancos y clientes en moneda extranjera al TC del último día) o sus pólizas si ya se corrió")
        .Produces<RevaluacionCierreContableResponse>().ProducesValidationProblem();

        g.MapPost("/", async (EjecutarRevaluacionCierreContableCommand b, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(b, ct)))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(capturar).WithName("EjecutarRevaluacionCierreContable")
        .WithSummary("Registra la revaluación del mes (una póliza por sucursal, fecha del último día); el día 1 siguiente se revierte sola")
        .Produces<RevaluacionCierreContableResponse>().ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        g.MapGet("/comprobacion", async ([FromQuery] int anio, [FromQuery] int mes, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ComprobarRevaluacionCierreQuery(anio, mes), ct)))
        .RequireAuthorization(leer).WithName("ComprobarRevaluacionCierreContable")
        .WithSummary("Comprobación posterior (M-317): saldo en divisa × TC de cierre contra el saldo contable por cuenta, sucursal y auxiliar")
        .Produces<ComprobacionRevaluacionResponse>().ProducesValidationProblem();

        g.MapPost("/anular", async (AnularRevaluacionCierreContableCommand b, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(b, ct)))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(capturar).WithName("AnularRevaluacionCierreContable")
        .WithSummary("Anula con motivo las pólizas de la revaluación del mes (periodo abierto y reversa sin correr); el mes puede correrse otra vez")
        .Produces<RevaluacionCierreContableResponse>().ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        g.MapPost("/reversas", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(new { Revertidas = await mediator.Send(new PublicarReversasRevaluacionContableCommand(), ct) }))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(capturar).WithName("RevertirRevaluacionesCierreContable")
        .WithSummary("Corre ahora la reversa del día 1 que el proceso de fondo hace cada hora");
        return app;
    }
}
