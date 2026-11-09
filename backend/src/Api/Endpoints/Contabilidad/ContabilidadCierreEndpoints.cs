using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Contabilidad.Application.Cierre;
using Millet.Contabilidad.Domain;
using Millet.Identidad.Domain;
using Helpers = Millet.Api.Endpoints.CentrosCosto.CentrosCostoCatalogoEndpoints;

namespace Millet.Api.Endpoints.Contabilidad;

/// <summary>
/// CON-12 (86e3jvvme) · checklist de cierre bajo <c>/api/v1/contabilidad/cierre</c>: evaluación por periodo (qué falta para cerrar),
/// excepciones autorizadas y confirmaciones de pasos manuales, configuración de los pasos, foto de cada cierre y cierre anual.
/// El cierre en sí sigue siendo <c>POST /periodos/{id}/cerrar</c>: ahí se exige el checklist (CA10.9).
/// </summary>
public static class ContabilidadCierreEndpoints
{
    public sealed record MarcaRequest(string Motivo);

    public sealed record PasoRequest(string Nombre, string? Descripcion, int Orden, bool Obligatorio, bool Activo);

    public sealed record PasoManualRequest(string Nombre, string? Descripcion, AlcancePasoCierre Alcance, int Orden, bool Obligatorio);

    public sealed record CierreAnualRequest(int Anio);

    public static IEndpointRouteBuilder MapContabilidadCierreEndpoints(this IEndpointRouteBuilder app)
    {
        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPeriodoLeer;
        var cerrar = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPeriodoCerrar;
        var excepcion = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadCierreAutorizarExcepcion;
        var configurar = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadCierreConfigurar;
        var anual = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadCierreAnual;

        var g = app.MapGroup("/api/v1/contabilidad/cierre").WithTags("Contabilidad · Cierre").RequireAuthorization();

        g.MapGet("/periodos/{periodoId:guid}/checklist", async (Guid periodoId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ObtenerChecklistCierreQuery(periodoId), ct)))
        .RequireAuthorization(leer).WithName("ChecklistCierrePeriodo")
        .WithSummary("Evalúa el checklist de cierre del periodo: estado de cada paso, qué falta y si se puede cerrar")
        .Produces<ChecklistCierreResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        g.MapPost("/periodos/{periodoId:guid}/pasos/{clave}/excepcion", async (Guid periodoId, string clave, MarcaRequest b, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new RegistrarMarcaPasoCierreCommand(periodoId, clave, TipoMarcaPasoCierre.Excepcion, b.Motivo), ct)))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(excepcion).WithName("AutorizarExcepcionCierre")
        .WithSummary("Autoriza cerrar el periodo con este paso pendiente (queda con nombre, fecha y motivo)")
        .Produces<MarcaPasoResponse>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        g.MapPost("/periodos/{periodoId:guid}/pasos/{clave}/confirmacion", async (Guid periodoId, string clave, MarcaRequest b, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new RegistrarMarcaPasoCierreCommand(periodoId, clave, TipoMarcaPasoCierre.Confirmacion, b.Motivo), ct)))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(cerrar).WithName("ConfirmarPasoManualCierre")
        .WithSummary("Confirma un paso manual del checklist con una nota")
        .Produces<MarcaPasoResponse>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        g.MapDelete("/marcas/{marcaId:guid}", async (Guid marcaId, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new RetirarMarcaPasoCierreCommand(marcaId), ct);
            return Results.NoContent();
        })
        .RequireAuthorization(cerrar).WithName("RetirarMarcaPasoCierre")
        .WithSummary("Retira una excepción o confirmación vigente (la excepción exige el permiso de autorizarla)")
        .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        g.MapGet("/registros/{cierreId:guid}", async (Guid cierreId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ObtenerCierreRegistradoQuery(cierreId), ct)))
        .RequireAuthorization(leer).WithName("CierreRegistradoPeriodo")
        .WithSummary("Foto del checklist guardada al cerrar (con las excepciones y quién las autorizó)")
        .Produces<ChecklistCierreResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        // ─── Configuración ───────────────────────────────────────────────────

        g.MapGet("/pasos", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarPasosCierreQuery(), ct)))
        .RequireAuthorization(leer).WithName("ListarPasosCierre")
        .Produces<IReadOnlyList<PasoCierreResponse>>();

        g.MapPost("/pasos", async (PasoManualRequest b, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new CrearPasoManualCierreCommand(b.Nombre, b.Descripcion, b.Alcance, b.Orden, b.Obligatorio), ct);
            return Results.Created($"/api/v1/contabilidad/cierre/pasos/{r.Id}", r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(configurar).WithName("CrearPasoManualCierre")
        .WithSummary("Agrega un paso manual al checklist")
        .Produces<PasoCierreResponse>(StatusCodes.Status201Created).ProducesValidationProblem();

        g.MapPut("/pasos/{id:guid}", async (Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, PasoRequest b, HttpResponse response,
            IMediator mediator, CancellationToken ct) =>
        {
            if (!Helpers.TryParseVersion(ifMatch, out var version)) return Helpers.IfMatchRequerido();
            var r = await mediator.Send(new ActualizarPasoCierreCommand(id, version, b.Nombre, b.Descripcion, b.Orden, b.Obligatorio, b.Activo), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Ok(r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(configurar).WithName("ActualizarPasoCierre")
        .WithSummary("Cambia nombre, orden, si es obligatorio y si está activo")
        .Produces<PasoCierreResponse>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status428PreconditionRequired);

        // ─── Cierre anual ────────────────────────────────────────────────────

        g.MapPost("/anual", async (CierreAnualRequest b, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new EjecutarCierreAnualCommand(b.Anio), ct)))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(anual).WithName("EjecutarCierreAnual")
        .WithSummary("Genera en el periodo 13 las pólizas (borrador) que cancelan las cuentas de resultados contra resultado del ejercicio")
        .Produces<CierreAnualResponse>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
