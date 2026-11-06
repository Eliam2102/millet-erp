using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Endpoints.Adjuntos;
using Millet.Api.Web;
using Millet.Compartido.Application.Adjuntos;
using Millet.Compartido.Application.Incidencias;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Incidencias;

namespace Millet.Api.Endpoints.Incidencias;

public sealed record NotaIncidenciaRequest(string? Nota);
public sealed record ResolverIncidenciaRequest(string? Resolucion);
public sealed record CancelarIncidenciaRequest(string? Motivo);
public sealed record ReasignarIncidenciaRequest(Guid ResponsableUsuarioId, string? Nota);

/// <summary>
/// Incidencias comunes del ERP (U-4 · 86e3yzemw): «mis incidencias», consulta por documento, alta manual y su ciclo
/// (atender, resolver, cancelar, reasignar) con ETag/If-Match (ADR-0012), Idempotency-Key (ADR-0020) y alcance por
/// sucursal (ADR-0051). La evidencia usa el servicio genérico de adjuntos bajo <c>/{id}/adjuntos</c>.
/// </summary>
public static class IncidenciasEndpoints
{
    public static IEndpointRouteBuilder MapIncidenciasEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/incidencias").WithTags("Incidencias");
        var ver = PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoIncidenciasVer;

        g.MapGet("/mias", async ([FromQuery] string[]? estado, [FromQuery] string? modulo, [FromQuery] int? offset, [FromQuery] int? limit,
                IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarMisIncidenciasQuery(estado, modulo, offset, limit), ct)))
            .RequireAuthorization()
            .WithName("ListarMisIncidencias")
            .Produces<IncidenciasPagina>();

        g.MapGet("/mias/resumen", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ResumenMisIncidenciasQuery(), ct)))
            .RequireAuthorization()
            .WithName("ResumenMisIncidencias")
            .Produces<ResumenMisIncidencias>();

        g.MapGet("/pasos", () => Results.Ok(PasosIncidencia.Todos))
            .RequireAuthorization()
            .WithName("ListarPasosIncidencia")
            .Produces<IReadOnlyList<PasoIncidencia>>();

        g.MapGet("/tipos-documento", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarTiposDocumentoQuery(IncidenciaAdjuntoPropietario.Tipo), ct)))
            .RequireAuthorization()
            .WithName("ListarTiposDocumentoIncidencia");

        g.MapGet("/", async ([FromQuery] string? modulo, [FromQuery] string? tipoDocumento, [FromQuery] Guid? documentoId,
                [FromQuery] string[]? estado, [FromQuery] Guid? sucursalId, [FromQuery] Guid? responsableId,
                [FromQuery] int? offset, [FromQuery] int? limit, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarIncidenciasQuery(modulo, tipoDocumento, documentoId, estado, sucursalId, responsableId, offset, limit), ct)))
            .RequireAuthorization(ver)
            .WithName("ListarIncidencias")
            .Produces<IncidenciasPagina>();

        // El responsable consulta la suya aunque no tenga «ver»: el handler resuelve el acceso.
        g.MapGet("/{id:guid}", async (Guid id, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            Detalle(await mediator.Send(new ObtenerIncidenciaQuery(id), ct), response))
            .RequireAuthorization()
            .WithName("ObtenerIncidencia")
            .Produces<IncidenciaDetalle>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        g.MapPost("/", async (AbrirIncidenciaCommand body, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            {
                var r = await mediator.Send(body, ct);
                response.Headers.ETag = Etag(r.Detalle.Version);
                return r.YaExistia
                    ? Results.Ok(r.Detalle)
                    : Results.Created($"/api/v1/incidencias/{r.Detalle.Id}", r.Detalle);
            })
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoIncidenciasRegistrar)
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .WithName("AbrirIncidencia")
            .Produces<IncidenciaDetalle>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        g.MapPost("/{id:guid}/atender", async (Guid id, NotaIncidenciaRequest? body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
                Version(request) is int v
                    ? Detalle(await mediator.Send(new AtenderIncidenciaCommand(id, v, body?.Nota), ct), response)
                    : FaltaVersion())
            .RequireAuthorization().WithMetadata(new RequireIdempotencyKeyAttribute()).WithName("AtenderIncidencia");

        g.MapPost("/{id:guid}/resolver", async (Guid id, ResolverIncidenciaRequest body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
                Version(request) is int v
                    ? Detalle(await mediator.Send(new ResolverIncidenciaCommand(id, v, body.Resolucion ?? string.Empty), ct), response)
                    : FaltaVersion())
            .RequireAuthorization().WithMetadata(new RequireIdempotencyKeyAttribute()).WithName("ResolverIncidencia");

        g.MapPost("/{id:guid}/cancelar", async (Guid id, CancelarIncidenciaRequest body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
                Version(request) is int v
                    ? Detalle(await mediator.Send(new CancelarIncidenciaCommand(id, v, body.Motivo ?? string.Empty), ct), response)
                    : FaltaVersion())
            .RequireAuthorization().WithMetadata(new RequireIdempotencyKeyAttribute()).WithName("CancelarIncidencia");

        g.MapPost("/{id:guid}/reasignar", async (Guid id, ReasignarIncidenciaRequest body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
                Version(request) is int v
                    ? Detalle(await mediator.Send(new ReasignarIncidenciaCommand(id, v, body.ResponsableUsuarioId, body.Nota), ct), response)
                    : FaltaVersion())
            .RequireAuthorization().WithMetadata(new RequireIdempotencyKeyAttribute()).WithName("ReasignarIncidencia");

        g.MapGroup("/{id:guid}").MapAdjuntos(IncidenciaAdjuntoPropietario.Tipo,
            PermisosCanonicos.CompartidoIncidenciasVer, PermisosCanonicos.CompartidoIncidenciasRegistrar, PermisosCanonicos.CompartidoIncidenciasGestionar);

        return app;
    }

    private static string Etag(int version) => $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";

    private static IResult Detalle(IncidenciaDetalle dto, HttpResponse response)
    {
        response.Headers.ETag = Etag(dto.Version);
        return Results.Ok(dto);
    }

    private static int? Version(HttpRequest request)
    {
        var raw = request.Headers.IfMatch.ToString();
        return raw.Length >= 3 && raw[0] == '"' && raw[^1] == '"'
            && int.TryParse(raw.AsSpan(1, raw.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
    }

    private static IResult FaltaVersion() => Results.Problem(statusCode: StatusCodes.Status428PreconditionRequired,
        title: "Envíe If-Match con el ETag vigente.", extensions: new Dictionary<string, object?> { ["code"] = "IF_MATCH_REQUIRED" });
}
