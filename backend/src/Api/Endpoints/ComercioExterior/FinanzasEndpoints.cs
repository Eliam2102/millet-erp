using System.Globalization;
using MediatR;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Domain;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.ComercioExterior;
public static class FinanzasEndpoints
{
    public static void MapFinanzasCe(this RouteGroupBuilder padre)
    {
        var gastos = PermissionPolicyProvider.Prefix + PermisosCanonicos.ComercioExteriorGastosRegistrar;
        var distribuir = PermissionPolicyProvider.Prefix + PermisosCanonicos.ComercioExteriorGastosDistribuir;
        var anticipos = PermissionPolicyProvider.Prefix + PermisosCanonicos.ComercioExteriorAnticiposGestionar;
        padre.MapGet("/finanzas", async (Guid id, FinanzasServicio servicio, HttpResponse response, CancellationToken ct) => Respuesta(await servicio.ObtenerAsync(id, ct), response))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComercioExteriorExpedientesLeer);
        padre.MapPost("/gastos/conceptos/inicializar", async (Guid id, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            await Mutar(new(id, 0, "inicializar-conceptos"), request, response, mediator, ct)).RequireAuthorization(gastos).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapPut("/gastos/conceptos/{conceptoId:guid}", async (Guid id, Guid conceptoId, ConceptoGastoDatos body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            await Mutar(new(id, 0, "concepto", conceptoId, Concepto: body), request, response, mediator, ct)).RequireAuthorization(gastos).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapPost("/gastos", async (Guid id, DatosGasto body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            await Mutar(new(id, 0, "gasto", Gasto: body), request, response, mediator, ct)).RequireAuthorization(gastos).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapPut("/gastos/{gastoId:guid}", async (Guid id, Guid gastoId, DatosGasto body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            await Mutar(new(id, 0, "gasto", gastoId, Gasto: body), request, response, mediator, ct)).RequireAuthorization(gastos).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapPut("/gastos/partidas", async (Guid id, PartidaCostoDatos body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            await Mutar(new(id, 0, "partida", Partida: body), request, response, mediator, ct)).RequireAuthorization(gastos).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapGet("/gastos/vista-previa", async (Guid id, HttpRequest request, FinanzasServicio servicio, CancellationToken ct) =>
            Version(request) is int version ? Results.Ok(await servicio.PreviaAsync(id, version, ct)) : FaltaVersion()).RequireAuthorization(distribuir);
        padre.MapPost("/gastos/distribuir", async (Guid id, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            await Mutar(new(id, 0, "distribuir"), request, response, mediator, ct)).RequireAuthorization(distribuir).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapPost("/anticipos", async (Guid id, AnticipoDatos body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            await Mutar(new(id, 0, "anticipo", Anticipo: body), request, response, mediator, ct)).RequireAuthorization(anticipos).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapPost("/anticipos/{anticipoId:guid}/comprobar", async (Guid id, Guid anticipoId, ComprobacionAnticipoDatos body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            await Mutar(new(id, 0, "comprobar", anticipoId, Comprobacion: body), request, response, mediator, ct)).RequireAuthorization(anticipos).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapPost("/anticipos/{anticipoId:guid}/resolver", async (Guid id, Guid anticipoId, ResolucionAnticipoDatos body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            await Mutar(new(id, 0, "resolver", anticipoId, Resolucion: body), request, response, mediator, ct)).RequireAuthorization(anticipos).WithMetadata(new RequireIdempotencyKeyAttribute());
    }
    private static int? Version(HttpRequest request)
    {
        var raw = request.Headers.IfMatch.ToString();
        return raw.Length >= 3 && raw[0] == '"' && raw[^1] == '"' && int.TryParse(raw.AsSpan(1, raw.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version > 0 ? version : null;
    }
    private static IResult FaltaVersion() => Results.Problem(statusCode: 428, title: "Envíe If-Match con el ETag vigente.", extensions: new Dictionary<string, object?> { ["code"] = "IF_MATCH_REQUIRED" });
    private static IResult Respuesta(FinanzasDetalle dto, HttpResponse response)
    { response.Headers.ETag = $"\"{dto.Version.ToString(CultureInfo.InvariantCulture)}\""; return Results.Ok(dto); }
    private static async Task<IResult> Mutar(MutarFinanzasCommand command, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        Version(request) is int version ? Respuesta(await mediator.Send(command with { Version = version }, ct), response) : FaltaVersion();
}
