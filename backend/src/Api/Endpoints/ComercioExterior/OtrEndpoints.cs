using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.ComercioExterior.Application;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.ComercioExterior;
public static class OtrEndpoints
{
    public static void MapOtrCe(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/comercio-exterior/otr").WithTags("Comercio exterior · OTR").RequireAuthorization();
        g.MapGet("/", async ([FromQuery] Guid? expedienteId, OtrServicio servicio, CancellationToken ct) => Results.Ok(await servicio.ListarAsync(expedienteId, ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComercioExteriorExpedientesLeer);
        var envio = g.MapGroup("/{id:guid}").WithDocumentoSucursalScope("envio_otr", "comercio_exterior.documentos")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComercioExteriorOtrGestionar);
        envio.MapGet("/expedientes", async (Guid id, OtrServicio servicio, CancellationToken ct) => Results.Ok(await servicio.CandidatosAsync(id, ct)));
        envio.MapPost("/vincular", async (Guid id, Vinculo body, HttpRequest req, HttpResponse res, IMediator mediator, CancellationToken ct) =>
            Version(req) is int v ? Respuesta(await mediator.Send(new VincularEnvioOtr(id, v, body.ExpedienteId), ct), res) : SinVersion())
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        envio.MapPost("/abrir-expediente", async (Guid id, Apertura body, HttpRequest req, HttpResponse res, IMediator mediator, CancellationToken ct) =>
            Version(req) is int v ? Respuesta(await mediator.Send(new AbrirExpedienteDesdeEnvioOtr(id, v, body.ResponsableUsuarioId, body.Moneda), ct), res) : SinVersion())
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        envio.MapPost("/conflictos/{conflictoId:guid}/resolver", async (Guid id, Guid conflictoId, Decision body, HttpRequest req, HttpResponse res, IMediator mediator, CancellationToken ct) =>
            Version(req) is int v ? Respuesta(await mediator.Send(new ResolverConflictoOtr(id, v, conflictoId, body.UsarOtr, body.Motivo), ct), res) : SinVersion())
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        envio.MapPost("/salidas/{salidaId:guid}/reintentar", async (Guid id, Guid salidaId, HttpRequest req, HttpResponse res, IMediator mediator, CancellationToken ct) =>
            Version(req) is int v ? Respuesta(await mediator.Send(new ReintentarSalidaOtr(id, v, salidaId), ct), res) : SinVersion())
            .WithMetadata(new RequireIdempotencyKeyAttribute());
    }
    private static int? Version(HttpRequest req)
    {
        var raw = req.Headers.IfMatch.ToString();
        return raw.Length >= 3 && raw[0] == '"' && raw[^1] == '"' && int.TryParse(raw.AsSpan(1, raw.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v >= 1 ? v : null;
    }
    private static IResult SinVersion() => Results.Problem(statusCode: 428, title: "Envíe If-Match con la versión vigente del envío.", extensions: new Dictionary<string, object?> { ["code"] = "IF_MATCH_REQUIRED" });
    private static IResult Respuesta(OtrDetalle d, HttpResponse response) { response.Headers.ETag = $"\"{d.Version.ToString(CultureInfo.InvariantCulture)}\""; return Results.Ok(d); }
    public sealed record Vinculo(Guid ExpedienteId);
    public sealed record Apertura(Guid ResponsableUsuarioId, string Moneda);
    public sealed record Decision(bool UsarOtr, string Motivo);
}
