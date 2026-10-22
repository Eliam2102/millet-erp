using System.Globalization;
using MediatR;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Domain;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.ComercioExterior;
public static class IncidenciasEndpoints
{
    public static void MapIncidenciasCe(this RouteGroupBuilder padre)
    {
        padre.MapGet("/incidencias", async (Guid id, IMediator mediator, HttpResponse response, CancellationToken ct) =>
            Respuesta(await mediator.Send(new ObtenerExpedienteCeQuery(id), ct), response))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComercioExteriorExpedientesLeer);
        padre.MapPost("/incidencias", async (Guid id, DatosIncidencia body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            Version(request) is int v ? Respuesta(await mediator.Send(new CrearIncidenciaCommand(id, v, body), ct), response) : FaltaVersion())
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComercioExteriorIncidenciasGestionar).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapPost("/incidencias/{incidenciaId:guid}/estado", async (Guid id, Guid incidenciaId, CambiarIncidenciaDatos body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            Version(request) is int v ? Respuesta(await mediator.Send(new CambiarIncidenciaCommand(id, incidenciaId, v, body), ct), response) : FaltaVersion())
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComercioExteriorIncidenciasGestionar).WithMetadata(new RequireIdempotencyKeyAttribute());
    }
    private static int? Version(HttpRequest request)
    {
        var raw = request.Headers.IfMatch.ToString();
        return raw.Length >= 3 && raw[0] == '"' && raw[^1] == '"' && int.TryParse(raw.AsSpan(1, raw.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
    }
    private static IResult FaltaVersion() => Results.Problem(statusCode: 428, title: "Envíe If-Match con el ETag vigente.", extensions: new Dictionary<string, object?> { ["code"] = "IF_MATCH_REQUIRED" });
    private static IResult Respuesta(ExpedienteDetalle dto, HttpResponse response)
    { response.Headers.ETag = $"\"{dto.Expediente.Version.ToString(CultureInfo.InvariantCulture)}\""; return Results.Ok(dto); }
}
