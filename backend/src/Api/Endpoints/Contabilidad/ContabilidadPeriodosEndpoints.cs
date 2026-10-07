using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Contabilidad.Application.Periodos;
using Millet.Identidad.Domain;
using Helpers = Millet.Api.Endpoints.CentrosCosto.CentrosCostoCatalogoEndpoints;

namespace Millet.Api.Endpoints.Contabilidad;

/// <summary>
/// Periodos contables (F1-CON-03, ficha C1.1) bajo <c>/api/v1/contabilidad/periodos</c>: crear el ejercicio (13 periodos),
/// cerrar y reabrir con historial. Mismas convenciones que el catálogo: ETag en GET por id, <c>If-Match</c> en cerrar/reabrir,
/// <c>Idempotency-Key</c> en POST.
/// </summary>
public static class ContabilidadPeriodosEndpoints
{
    public sealed record CrearEjercicioRequest(int Ejercicio);

    public sealed record CerrarPeriodoRequest(string? Motivo);

    public sealed record ReabrirPeriodoRequest(string Motivo);

    public static IEndpointRouteBuilder MapContabilidadPeriodosEndpoints(this IEndpointRouteBuilder app)
    {
        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPeriodoLeer;
        var administrar = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPeriodoAdministrar;
        var cerrar = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPeriodoCerrar;
        var reabrir = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPeriodoReabrir;

        var g = app.MapGroup("/api/v1/contabilidad/periodos").WithTags("Contabilidad · Periodos").RequireAuthorization();

        g.MapGet("", async ([FromQuery] int ejercicio, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarPeriodosQuery(ejercicio), ct)))
        .RequireAuthorization(leer).WithName("ListarPeriodosContables")
        .WithSummary("Los 13 periodos de un ejercicio; lista vacía si el ejercicio no se ha creado")
        .Produces<IReadOnlyList<PeriodoResponse>>();

        g.MapGet("/ejercicios", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarEjerciciosQuery(), ct)))
        .RequireAuthorization(leer).WithName("ListarEjerciciosContables")
        .Produces<IReadOnlyList<int>>();

        g.MapPost("/ejercicios", async (CrearEjercicioRequest b, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new CrearEjercicioCommand(b.Ejercicio), ct);
            return Results.Created($"/api/v1/contabilidad/periodos?ejercicio={r.Ejercicio}", r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar).WithName("CrearEjercicioContable")
        .Produces<EjercicioResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem().ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        g.MapGet("/{id:guid}", async (Guid id, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new ObtenerPeriodoQuery(id), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Ok(r);
        })
        .RequireAuthorization(leer).WithName("ObtenerPeriodoContable")
        .Produces<PeriodoResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        g.MapGet("/{id:guid}/historial", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new HistorialPeriodoQuery(id), ct)))
        .RequireAuthorization(leer).WithName("HistorialPeriodoContable")
        .Produces<IReadOnlyList<PeriodoEventoResponse>>().ProducesProblem(StatusCodes.Status404NotFound);

        g.MapPost("/{id:guid}/cerrar", async (
            Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, CerrarPeriodoRequest b, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!Helpers.TryParseVersion(ifMatch, out var version)) return Helpers.IfMatchRequerido();
            var r = await mediator.Send(new CerrarPeriodoCommand(id, version, b.Motivo), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Ok(r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(cerrar).WithName("CerrarPeriodoContable")
        .Produces<PeriodoResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesProblem(StatusCodes.Status428PreconditionRequired);

        g.MapPost("/{id:guid}/reabrir", async (
            Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, ReabrirPeriodoRequest b, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!Helpers.TryParseVersion(ifMatch, out var version)) return Helpers.IfMatchRequerido();
            var r = await mediator.Send(new ReabrirPeriodoCommand(id, version, b.Motivo), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Ok(r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(reabrir).WithName("ReabrirPeriodoContable")
        .WithSummary("Solo el Contador General (permiso contabilidad.periodo.reabrir), con motivo obligatorio")
        .Produces<PeriodoResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesValidationProblem().ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        return app;
    }
}
