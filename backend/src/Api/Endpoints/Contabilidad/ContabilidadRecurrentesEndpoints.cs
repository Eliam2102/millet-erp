using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;
using Millet.Identidad.Domain;
using Helpers = Millet.Api.Endpoints.CentrosCosto.CentrosCostoCatalogoEndpoints;

namespace Millet.Api.Endpoints.Contabilidad;

/// <summary>
/// Pólizas recurrentes (K10.4) bajo <c>/api/v1/contabilidad/polizas-recurrentes</c>: plantillas con importe, periodos y reversa
/// automática; activación por otra persona (R23); «generar» corre ahora lo que el proceso de fondo genera cada hora.
/// </summary>
public static class ContabilidadRecurrentesEndpoints
{
    public static IEndpointRouteBuilder MapContabilidadRecurrentesEndpoints(this IEndpointRouteBuilder app)
    {
        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadRecurrentesLeer;
        var administrar = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadRecurrentesAdministrar;
        var autorizar = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadRecurrentesAutorizar;

        var g = app.MapGroup("/api/v1/contabilidad/polizas-recurrentes").WithTags("Contabilidad · Pólizas recurrentes").RequireAuthorization();

        g.MapGet("/", async ([FromQuery] EstadoPlantillaRecurrente? estado, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarPlantillasRecurrentesQuery(estado), ct)))
        .RequireAuthorization(leer).WithName("ListarPlantillasRecurrentes")
        .Produces<IReadOnlyList<PlantillaRecurrenteResumenResponse>>();

        g.MapGet("/{id:guid}", async (Guid id, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new ObtenerPlantillaRecurrenteQuery(id), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Ok(r);
        })
        .RequireAuthorization(leer).WithName("ObtenerPlantillaRecurrente")
        .Produces<PlantillaRecurrenteResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        g.MapPost("/", async (PlantillaRecurrenteRequest b, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new CrearPlantillaRecurrenteCommand(b), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Created($"/api/v1/contabilidad/polizas-recurrentes/{r.Id}", r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar).WithName("CrearPlantillaRecurrente")
        .WithSummary("Prepara una plantilla recurrente en borrador (importe, periodos, día, reversa automática)")
        .Produces<PlantillaRecurrenteResponse>(StatusCodes.Status201Created).ProducesValidationProblem().ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        g.MapPut("/{id:guid}", async (
            Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, PlantillaRecurrenteRequest b, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!Helpers.TryParseVersion(ifMatch, out var version)) return Helpers.IfMatchRequerido();
            var r = await mediator.Send(new EditarPlantillaRecurrenteCommand(id, version, b), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Ok(r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar).WithName("EditarPlantillaRecurrente")
        .Produces<PlantillaRecurrenteResponse>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesProblem(StatusCodes.Status428PreconditionRequired);

        MapCambio(g, "activar", AccionPlantillaRecurrente.Activar, autorizar, "Activa la plantilla (otra persona, no quien la preparó) y genera lo que ya toca");
        MapCambio(g, "suspender", AccionPlantillaRecurrente.Suspender, administrar, "Suspende la generación");
        MapCambio(g, "reanudar", AccionPlantillaRecurrente.Reanudar, administrar, "Reanuda la generación y genera lo pendiente");

        g.MapPost("/generar", async ([FromQuery] Guid? plantillaId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GenerarRecurrentesCommand(plantillaId), ct)))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar).WithName("GenerarPolizasRecurrentes")
        .WithSummary("Genera ahora las pólizas y reversas que ya tocan (idempotente); devuelve las generadas y las pendientes con su motivo")
        .Produces<ResultadoGeneracionRecurrentes>();
        return app;
    }

    private static void MapCambio(RouteGroupBuilder g, string ruta, AccionPlantillaRecurrente accion, string politica, string resumen) =>
        g.MapPost($"/{{id:guid}}/{ruta}", async (
            Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!Helpers.TryParseVersion(ifMatch, out var version)) return Helpers.IfMatchRequerido();
            var r = await mediator.Send(new CambiarEstadoPlantillaRecurrenteCommand(id, version, accion), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Ok(r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(politica).WithName($"PlantillaRecurrente_{ruta}").WithSummary(resumen)
        .Produces<PlantillaRecurrenteResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesProblem(StatusCodes.Status428PreconditionRequired);
}
