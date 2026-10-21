using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Api.Endpoints.Adjuntos;
using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Domain;
using Millet.Compras.Application.PublicPorts;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.ComercioExterior;
public static class ExpedientesEndpoints
{
    private const string Base = "/api/v1/comercio-exterior/expedientes";
    public static IEndpointRouteBuilder MapExpedientesCe(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup(Base).WithTags("Comercio exterior").RequireAuthorization();
        g.MapGet("/", async ([FromQuery] bool? atraso, [FromQuery] TipoExpediente? tipo, [FromQuery] EstadoExpediente? estado, [FromQuery] Guid? sucursalId, [FromQuery] string? q, [FromQuery] int? offset, [FromQuery] int? limit, IMediator mediator, CancellationToken ct) => Results.Ok(await mediator.Send(new ListarExpedientesQuery(tipo, estado, sucursalId, q, offset ?? 0, limit ?? 10, atraso), ct))).RequireAuthorization(Policy(PermisosCanonicos.ComercioExteriorExpedientesLeer));
        g.MapGet("/ordenes-importacion", async ([FromQuery] string? q, ExpedienteServicio servicio, CancellationToken ct) => Results.Ok(await servicio.OrdenesImportacionAsync(q, ct))).RequireAuthorization(Policy(PermisosCanonicos.ComercioExteriorExpedientesCrear));
        g.MapGet("/referencias/{tipo}", async (string tipo, [FromQuery] string? q, ICatalogosCeReadPort catalogos, CancellationToken ct) => Results.Ok(await catalogos.ReferenciasAsync(tipo, (q ?? "").Trim(), ct))).RequireAuthorization(Policy(PermisosCanonicos.ComercioExteriorExpedientesEditar));
        g.MapPost("/", async (CrearExpedienteCommand body, IMediator mediator, HttpResponse response, CancellationToken ct) => Respuesta(await mediator.Send(body, ct), response, true)).RequireAuthorization(Policy(PermisosCanonicos.ComercioExteriorExpedientesCrear)).WithMetadata(new RequireIdempotencyKeyAttribute());
        app.MapGet("/api/v1/comercio-exterior/relacion-iva", async ([FromQuery] int anio, [FromQuery] int mes, [FromQuery] Guid? sucursalId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new RelacionIvaQuery(anio, mes, sucursalId), ct)))
            .RequireAuthorization(Policy(PermisosCanonicos.ComercioExteriorRelacionIvaLeer));
        var padre = g.MapGroup("/{id:guid}").WithDocumentoSucursalScope("expediente_ce", "comercio_exterior.documentos");
        padre.MapContenedoresCe();
        padre.MapFinanzasCe();
        padre.MapGet("/", async (Guid id, IMediator mediator, HttpResponse response, CancellationToken ct) => Respuesta(await mediator.Send(new ObtenerExpedienteCeQuery(id), ct), response)).RequireAuthorization(Policy(PermisosCanonicos.ComercioExteriorExpedientesLeer));
        padre.MapPatch("/", async (Guid id, EditarExpedienteDatos body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) => await Mutar(id, "editar", request, response, mediator, ct, datos: body)).RequireAuthorization(Policy(PermisosCanonicos.ComercioExteriorExpedientesEditar)).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapPost("/participantes", async (Guid id, ParticipanteDatos body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) => await Mutar(id, "participantes", request, response, mediator, ct, participante: body)).RequireAuthorization(Policy(PermisosCanonicos.ComercioExteriorExpedientesEditar)).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapPatch("/participantes/{participanteId:guid}", async (Guid id, Guid participanteId, ParticipanteDatos body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) => await Mutar(id, "participantes", request, response, mediator, ct, participante: body, participanteId: participanteId)).RequireAuthorization(Policy(PermisosCanonicos.ComercioExteriorExpedientesEditar)).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapPost("/documentos", async (Guid id, DocumentoDatos body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) => await Mutar(id, "documentos", request, response, mediator, ct, documento: body)).RequireAuthorization(Policy(PermisosCanonicos.ComercioExteriorExpedientesAdjuntosSubir)).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapPost("/revisar-documentos", async (Guid id, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) => await Mutar(id, "revisar-documentos", request, response, mediator, ct)).RequireAuthorization(Policy(PermisosCanonicos.ComercioExteriorExpedientesDocumentosRevisar)).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapPost("/cancelar", async (Guid id, MotivoRequest body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) => await Mutar(id, "cancelar", request, response, mediator, ct, motivo: body.Motivo)).RequireAuthorization(Policy(PermisosCanonicos.ComercioExteriorExpedientesCancelar)).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapPost("/cerrar", async (Guid id, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) => await Mutar(id, "cerrar", request, response, mediator, ct)).RequireAuthorization(Policy(PermisosCanonicos.ComercioExteriorExpedientesCerrar)).WithMetadata(new RequireIdempotencyKeyAttribute());
        foreach (var (ruta, accion) in new[] { ("compromiso", "hito-compromiso"), ("real", "hito-real"), ("omitir", "hito-omitir") })
            padre.MapPost("/hitos/{clave}/" + ruta, async (Guid id, string clave, HitoDatos body, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct) => await Mutar(id, accion, request, response, mediator, ct, hito: body with { Clave = clave })).RequireAuthorization(Policy(PermisosCanonicos.ComercioExteriorExpedientesHitosRegistrar)).WithMetadata(new RequireIdempotencyKeyAttribute());
        padre.MapGet("/bitacora", async (Guid id, IMediator mediator, ExpedienteServicio servicio, Millet.SharedKernel.Application.IClock clock, CancellationToken ct) =>
        {
            await servicio.CargarAsync(id, false, ct);
            var hoy = Millet.SharedKernel.Application.FechaContable.HoyLocal(clock.UtcNow);
            return Results.Ok(await mediator.Send(new Millet.Administracion.Application.Auditoria.ConsultarBitacoraQuery(hoy.AddDays(-90), hoy, Modulo: "ComercioExterior", AggregateRootId: id), ct));
        }).RequireAuthorization(Policy(PermisosCanonicos.ComercioExteriorExpedientesLeer));
        padre.MapAdjuntos("expediente_ce", PermisosCanonicos.ComercioExteriorExpedientesAdjuntosVer, PermisosCanonicos.ComercioExteriorExpedientesAdjuntosSubir, PermisosCanonicos.ComercioExteriorExpedientesAdjuntosBaja);
        return app;
    }

    private static string Policy(string p) => PermissionPolicyProvider.Prefix + p;
    private static IResult Respuesta(ExpedienteDetalle dto, HttpResponse response, bool creado = false)
    {
        response.Headers.ETag = $"\"{dto.Expediente.Version.ToString(CultureInfo.InvariantCulture)}\"";
        return creado ? Results.Created($"{Base}/{dto.Expediente.Id}", dto) : Results.Ok(dto);
    }

    private static async Task<IResult> Mutar(Guid id, string accion, HttpRequest request, HttpResponse response, IMediator mediator, CancellationToken ct, EditarExpedienteDatos? datos = null, ParticipanteDatos? participante = null, Guid? participanteId = null, DocumentoDatos? documento = null, string? motivo = null, HitoDatos? hito = null)
    {
        var raw = request.Headers.IfMatch.ToString();
        if (raw.Length < 3 || raw[0] != '"' || raw[^1] != '"' || !int.TryParse(raw.AsSpan(1, raw.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
            return Results.Problem(statusCode: 428, title: "Envíe If-Match con el ETag vigente.", extensions: new Dictionary<string, object?> { ["code"] = "IF_MATCH_REQUIRED" });
        try
        {
            return Respuesta(await mediator.Send(new MutarExpedienteCommand(id, version, accion, datos, participante, participanteId, documento, motivo, hito), ct), response);
        }
        catch (DocumentosFaltantesException ex)
        {
            return Results.Problem(statusCode: 422, title: ex.Message, extensions: new Dictionary<string, object?> { ["code"] = ex.Code, ["faltantes"] = ex.Faltantes });
        }
    }

    public sealed record MotivoRequest(string Motivo);
}
