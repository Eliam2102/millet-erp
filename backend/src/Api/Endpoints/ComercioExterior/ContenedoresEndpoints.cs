using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Domain;

namespace Millet.Api.Endpoints.ComercioExterior;
public static class ContenedoresEndpoints
{
    public static void MapContenedoresCe(this RouteGroupBuilder padre)
    {
        var gestionar = PermissionPolicyProvider.Prefix + Millet.Identidad.Domain.PermisosCanonicos.ComercioExteriorContenedoresGestionar;
        padre.MapGet("/contenedores", async (Guid id, ContenedoresServicio servicio, HttpResponse response, CancellationToken ct) =>
            Respuesta(await servicio.ObtenerAsync(id, ct), response))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + Millet.Identidad.Domain.PermisosCanonicos.ComercioExteriorExpedientesLeer);
        padre.MapGet("/pedidos-embarcables", async (Guid id, [FromQuery] Guid clienteId, ContenedoresServicio servicio, CancellationToken ct) =>
            Results.Ok(await servicio.ListarPedidosAsync(id, clienteId, ct))).RequireAuthorization(gestionar);
        padre.MapPost("/contenedores", async (Guid id, DatosContenedor body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            await Mutar(new(id, 0, "guardar", Datos: body), request, response, mediator, ct)).RequireAuthorization(gestionar).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapPatch("/contenedores/{contenedorId:guid}", async (Guid id, Guid contenedorId, DatosContenedor body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            await Mutar(new(id, 0, "guardar", contenedorId, body), request, response, mediator, ct)).RequireAuthorization(gestionar).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapPut("/pedimento-exportacion", async (Guid id, PedimentoExportacionDatos body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            await Mutar(new(id, 0, "pedimento", Pedimento: body), request, response, mediator, ct)).RequireAuthorization(gestionar).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapPost("/contenedores/{contenedorId:guid}/partidas", async (Guid id, Guid contenedorId, PartidaDatos body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            await Mutar(new(id, 0, "asignar", contenedorId, Partida: body), request, response, mediator, ct)).RequireAuthorization(gestionar).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapPost("/partidas/{partidaId:guid}/confirmar", async (Guid id, Guid partidaId, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            await Mutar(new(id, 0, "confirmar", PartidaId: partidaId), request, response, mediator, ct)).RequireAuthorization(gestionar).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapDelete("/partidas/{partidaId:guid}", async (Guid id, Guid partidaId, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            await Mutar(new(id, 0, "quitar-partida", PartidaId: partidaId), request, response, mediator, ct)).RequireAuthorization(gestionar).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapPost("/contenedores/{contenedorId:guid}/embarcar", async (Guid id, Guid contenedorId, FechaEmbarqueDatos body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            await Mutar(new(id, 0, "embarcar", contenedorId, Fecha: body.Fecha), request, response, mediator, ct)).RequireAuthorization(gestionar).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapPost("/contenedores/{contenedorId:guid}/cancelar", async (Guid id, Guid contenedorId, ExpedientesEndpoints.MotivoRequest body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) =>
            await Mutar(new(id, 0, "cancelar", contenedorId, Motivo: body.Motivo), request, response, mediator, ct)).RequireAuthorization(gestionar).WithMetadata(new RequireIdempotencyKeyAttribute());
    }
    private static IResult Respuesta(ContenedoresDetalle d, HttpResponse response)
    {
        response.Headers.ETag = $"\"{d.Version.ToString(CultureInfo.InvariantCulture)}\"";
        return Results.Ok(d);
    }
    private static async Task<IResult> Mutar(MutarContenedorCommand command, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct)
    {
        var raw = request.Headers.IfMatch.ToString();
        if (raw.Length < 3 || raw[0] != '"' || raw[^1] != '"' || !int.TryParse(raw.AsSpan(1, raw.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
            return Results.Problem(statusCode: 428, title: "Envíe If-Match con el ETag vigente.", extensions: new Dictionary<string, object?> { ["code"] = "IF_MATCH_REQUIRED" });
        return Respuesta(await mediator.Send(command with { Version = version }, ct), response);
    }
    public sealed record FechaEmbarqueDatos(DateOnly Fecha);
}
