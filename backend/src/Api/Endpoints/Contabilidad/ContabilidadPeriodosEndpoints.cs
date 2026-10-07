using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Contabilidad.Application.Periodos;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Identidad.Domain;
using Helpers = Millet.Api.Endpoints.CentrosCosto.CentrosCostoCatalogoEndpoints;

namespace Millet.Api.Endpoints.Contabilidad;

/// <summary>
/// Periodos contables (F1-CON-03) bajo <c>/api/v1/contabilidad/periodos</c>: ejercicios, apertura en lote, cierre y reapertura
/// con motivo y bitácora. ETag en GET por id; <c>If-Match</c> (versión) e <c>Idempotency-Key</c> en las mutaciones. El periodo es
/// de la empresa, no de una sucursal: sin <c>SucursalScopeGuard</c> (ADR-0051 no aplica).
/// </summary>
public static class ContabilidadPeriodosEndpoints
{
    public sealed record CrearEjercicioRequest(int Anio);

    public sealed record AbrirPeriodosRequest(IReadOnlyList<int> Numeros, string? Motivo);

    public sealed record MotivoRequest(string Motivo);

    public static IEndpointRouteBuilder MapContabilidadPeriodosEndpoints(this IEndpointRouteBuilder app)
    {
        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPeriodoLeer;
        var administrar = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPeriodoAdministrar;
        var cerrar = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPeriodoCerrar;
        var reabrir = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPeriodoReabrir;

        var g = app.MapGroup("/api/v1/contabilidad/periodos").WithTags("Contabilidad · Periodos").RequireAuthorization();

        // ─── Ejercicios ──────────────────────────────────────────────────────

        g.MapGet("/ejercicios", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarEjerciciosQuery(), ct)))
        .RequireAuthorization(leer).WithName("ListarEjerciciosContables")
        .Produces<IReadOnlyList<EjercicioContableResponse>>();

        g.MapPost("/ejercicios", async (CrearEjercicioRequest b, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new CrearEjercicioContableCommand(b.Anio), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Created($"/api/v1/contabilidad/periodos/ejercicios/{r.Id}", r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar).WithName("CrearEjercicioContable")
        .WithSummary("Crea el ejercicio con sus 12 periodos y el 13 de ajustes, todos sin abrir")
        .Produces<EjercicioContableResponse>(StatusCodes.Status201Created).ProducesValidationProblem().ProducesProblem(StatusCodes.Status409Conflict);

        g.MapGet("/ejercicios/{id:guid}", async (Guid id, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new ObtenerEjercicioQuery(id), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Ok(r);
        })
        .RequireAuthorization(leer).WithName("ObtenerEjercicioContable")
        .Produces<EjercicioContableResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        g.MapPost("/ejercicios/{id:guid}/abrir", async (
            Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, AbrirPeriodosRequest b, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!Helpers.TryParseVersion(ifMatch, out var version)) return Helpers.IfMatchRequerido();
            var r = await mediator.Send(new AbrirPeriodosCommand(id, version, b.Numeros ?? [], b.Motivo), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Ok(r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar).WithName("AbrirPeriodosContables")
        .WithSummary("Abre en lote periodos sin abrir del ejercicio (If-Match = versión del ejercicio); todo o nada")
        .Produces<EjercicioContableResponse>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        // ─── Periodo ─────────────────────────────────────────────────────────

        g.MapPost("/{periodoId:guid}/cerrar", async (
            Guid periodoId, [FromHeader(Name = "If-Match")] string? ifMatch, MotivoRequest b, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!Helpers.TryParseVersion(ifMatch, out var version)) return Helpers.IfMatchRequerido();
            var r = await mediator.Send(new CerrarPeriodoCommand(periodoId, version, b.Motivo), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Ok(r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(cerrar).WithName("CerrarPeriodoContable")
        .WithSummary("Cierra un periodo abierto con motivo; 409 si cambió la versión o ya está cerrado")
        .Produces<PeriodoContableResponse>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        g.MapPost("/{periodoId:guid}/reabrir", async (
            Guid periodoId, [FromHeader(Name = "If-Match")] string? ifMatch, MotivoRequest b, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!Helpers.TryParseVersion(ifMatch, out var version)) return Helpers.IfMatchRequerido();
            var r = await mediator.Send(new ReabrirPeriodoCommand(periodoId, version, b.Motivo), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Ok(r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(reabrir).WithName("ReabrirPeriodoContable")
        .WithSummary("Reabre un periodo cerrado con motivo (no reabre el inventario)")
        .Produces<PeriodoContableResponse>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesProblem(StatusCodes.Status428PreconditionRequired);

        g.MapGet("/estado", async ([FromQuery] DateOnly? fecha, [FromQuery] int? anio, [FromQuery] int? numero, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ConsultarEstadoPeriodoQuery(fecha, anio, numero), ct)))
        .RequireAuthorization(leer).WithName("ConsultarEstadoPeriodoContable")
        .WithSummary("Estado del periodo de una fecha (1–12) o por año y número (1–13); mismo contrato que IPeriodoContableConsultaPort")
        .Produces<EstadoPeriodoContable>().ProducesValidationProblem();

        g.MapGet("/{periodoId:guid}/bitacora", async (Guid periodoId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ObtenerBitacoraPeriodoQuery(periodoId), ct)))
        .RequireAuthorization(leer).WithName("BitacoraPeriodoContable")
        .Produces<IReadOnlyList<BitacoraPeriodoResponse>>().ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
