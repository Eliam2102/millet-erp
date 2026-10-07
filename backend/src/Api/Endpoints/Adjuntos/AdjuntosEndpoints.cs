using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Compartido.Application.Adjuntos;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Application.Auditoria;

namespace Millet.Api.Endpoints.Adjuntos;

/// <summary>Cuerpo del DELETE de un adjunto.</summary>
public sealed record DarDeBajaAdjuntoRequest(string? Motivo);

/// <summary>
/// Servicio genérico de adjuntos (F1-ADM-11 G1.2). <see cref="MapAdjuntos"/> cuelga las rutas bajo el grupo del
/// padre (<c>.../{id}</c>); el permiso de rol va declarativo y el alcance/existencia del padre lo resuelve
/// <see cref="AdjuntoAcceso"/> en cada handler (contrato adm-11 §3). Nunca se expone el <c>BlobRef</c>.
/// </summary>
public static class AdjuntosEndpoints
{
    public const string CodigoEnlaceInvalido = "ADJUNTO_ENLACE_INVALIDO";

    /// <summary>
    /// Registra las rutas de adjuntos bajo <paramref name="padre"/> (grupo con <c>{id:guid}</c> de la entidad dueña).
    /// Los permisos son los de la entidad (el propietario de la entidad los repite para la capa 2).
    /// </summary>
    public static RouteGroupBuilder MapAdjuntos(
        this RouteGroupBuilder padre, string tipoEntidad, string permisoVer, string permisoSubir, string permisoBaja)
    {
        var ver = PermissionPolicyProvider.Prefix + permisoVer;
        var subir = PermissionPolicyProvider.Prefix + permisoSubir;
        var baja = PermissionPolicyProvider.Prefix + permisoBaja;

        padre.MapPost("/adjuntos", async (
            Guid id,
            [FromForm] Guid tipoDocumentoId,
            [FromForm] DateOnly? vigenteHasta,
            IFormFile? archivo,
            IMediator mediator,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (archivo is null || archivo.Length == 0)
            {
                throw new BusinessRuleException(
                    "ARCHIVO_REQUERIDO", "Debe enviarse un archivo en el campo 'archivo' del multipart.");
            }

            await using var stream = archivo.OpenReadStream();
            var response = await mediator.Send(new SubirAdjuntoCommand(
                tipoEntidad, id, tipoDocumentoId, archivo.FileName, archivo.ContentType ?? "application/octet-stream",
                stream, vigenteHasta), ct);
            return Results.Created($"{http.Request.Path.Value!.TrimEnd('/')}/{response.Id}", response);
        })
        .DisableAntiforgery()
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(subir)
        .WithName($"SubirAdjunto_{tipoEntidad}")
        .WithSummary("Subir un adjunto al expediente (multipart/form-data)")
        .WithDescription(
            "Campos del multipart: `archivo`, `tipoDocumentoId` (catálogo de tipos) y `vigenteHasta` opcional " +
            "(si falta y el tipo tiene vigencia, se calcula). Valida tamaño, extensión, MIME y firma del archivo. " +
            "Requiere `Idempotency-Key`. 422 por archivo no permitido o regla de negocio; 404 si el padre no existe.")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<AdjuntoResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        padre.MapGet("/adjuntos", async (
            Guid id, [FromQuery] bool? incluirBajas, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarAdjuntosQuery(tipoEntidad, id, incluirBajas ?? false), ct)))
        .RequireAuthorization(ver)
        .WithName($"ListarAdjuntos_{tipoEntidad}")
        .WithSummary("Listar los adjuntos de la entidad")
        .WithDescription("`incluirBajas=true` exige además el permiso de baja. Incluye estado derivado de vigencia.")
        .Produces<IReadOnlyList<AdjuntoResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        padre.MapGet("/adjuntos/{adjuntoId:guid}", async (
            Guid id, Guid adjuntoId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ObtenerAdjuntoQuery(tipoEntidad, id, adjuntoId), ct)))
        .RequireAuthorization(ver)
        .WithName($"ObtenerAdjunto_{tipoEntidad}")
        .WithSummary("Metadatos de un adjunto")
        .WithDescription("404 si no existe o pertenece a otra entidad (sin IDOR). Uno dado de baja solo lo ve quien puede dar de baja.")
        .Produces<AdjuntoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        padre.MapGet("/adjuntos/{adjuntoId:guid}/contenido", async (
            Guid id, Guid adjuntoId, IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            var r = await mediator.Send(new ObtenerContenidoAdjuntoQuery(tipoEntidad, id, adjuntoId), ct);
            return ArchivoSeguro(r, http);
        })
        .RequireAuthorization(ver)
        .WithName($"ContenidoAdjunto_{tipoEntidad}")
        .WithSummary("Descargar el contenido de un adjunto (stream autenticado)")
        .WithDescription("Hace stream por el backend; nunca expone la ubicación del blob. Audita la descarga.")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        padre.MapPost("/adjuntos/{adjuntoId:guid}/enlace", async (
            Guid id, Guid adjuntoId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new EmitirEnlaceDescargaCommand(tipoEntidad, id, adjuntoId), ct)))
        .RequireAuthorization(ver)
        .WithName($"EnlaceAdjunto_{tipoEntidad}")
        .WithSummary("Emitir un enlace temporal de descarga (60 s)")
        .WithDescription(
            "Devuelve `{ url, expiraEn }`. El token está atado al adjunto y al usuario que lo pidió y expira en 60 s.")
        .Produces<EnlaceDescargaResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        padre.MapDelete("/adjuntos/{adjuntoId:guid}", async (
            Guid id, Guid adjuntoId, [FromBody] DarDeBajaAdjuntoRequest body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new DarDeBajaAdjuntoCommand(tipoEntidad, id, adjuntoId, body?.Motivo ?? string.Empty), ct)))
        .RequireAuthorization(baja)
        .WithName($"BajaAdjunto_{tipoEntidad}")
        .WithSummary("Dar de baja un adjunto (baja lógica con motivo)")
        .WithDescription("Motivo obligatorio (5-500 caracteres). Irreversible; el archivo físico se conserva. 422 si ya estaba dado de baja.")
        .Produces<AdjuntoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        padre.MapGet("/adjuntos/{adjuntoId:guid}/bitacora", async (
            Guid id, Guid adjuntoId, [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta,
            [FromQuery] int? offset, [FromQuery] int? limit, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ConsultarBitacoraAdjuntoQuery(
                tipoEntidad, id, adjuntoId, desde, hasta, offset ?? 0, limit ?? 50), ct)))
        .RequireAuthorization(baja)
        .WithName($"BitacoraAdjunto_{tipoEntidad}")
        .WithSummary("Bitácora de un adjunto (quién, cuándo, qué, por qué)")
        .WithDescription("Requiere el permiso de baja. Rango por defecto: desde el alta hasta hoy (máx. 90 días).")
        .Produces<ConsultarBitacoraResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        padre.MapGet("/expediente", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ObtenerExpedienteQuery(tipoEntidad, id), ct)))
        .RequireAuthorization(ver)
        .WithName($"Expediente_{tipoEntidad}")
        .WithSummary("Expediente documental por tipo (Faltante/Vigente/PorVencer/Vencido)")
        .WithDescription("`completo` = todos los tipos obligatorios aplicables están vigentes o por vencer.")
        .Produces<ExpedienteResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return padre;
    }

    /// <summary>Rutas transversales: catálogo de tipos y descarga anónima por enlace temporal.</summary>
    public static IEndpointRouteBuilder MapAdjuntosGenerales(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/adjuntos").WithTags("Adjuntos");

        g.MapGet("/tipos", async ([FromQuery] string entidad, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarTiposDocumentoQuery(entidad), ct)))
        .RequireAuthorization()
        .WithName("ListarTiposDocumentoAdjunto")
        .WithSummary("Catálogo de tipos de documento de un tipo de entidad")
        .WithDescription("`entidad` = tipo de entidad (p. ej. `proveedor`). Requiere el permiso de ver de esa entidad; desconocida -> 404.")
        .Produces<IReadOnlyList<AdjuntoTipoDocumentoResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        g.MapGet("/descargas/{token}", async (string token, IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            var r = await mediator.Send(new ObtenerContenidoPorEnlaceQuery(token), ct);
            if (r is null)
            {
                return Results.Problem(
                    title: "El enlace de descarga no es válido o expiró.",
                    detail: "El enlace de descarga no es válido o expiró.",
                    statusCode: StatusCodes.Status401Unauthorized,
                    type: $"https://millet-erp/errors/{CodigoEnlaceInvalido.ToLowerInvariant()}",
                    extensions: new Dictionary<string, object?> { ["code"] = CodigoEnlaceInvalido });
            }
            return ArchivoSeguro(r, http);
        })
        .AllowAnonymous()
        .WithName("DescargarAdjuntoPorEnlace")
        .WithSummary("Descargar por enlace temporal (el token es la credencial)")
        .WithDescription(
            "Token inválido, manipulado o expirado -> 401 `ADJUNTO_ENLACE_INVALIDO`. Re-verifica que el adjunto " +
            "no tenga baja (404). Se audita con el usuario que emitió el enlace.")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    // El nombre ya viene sin ruta desde la subida; aquí solo se neutralizan control y separadores antes de que
    // Results.File lo codifique (RFC 6266/5987) en Content-Disposition. nosniff evita que el navegador reinterprete el MIME.
    private static IResult ArchivoSeguro(ContenidoAdjuntoResponse r, HttpContext http)
    {
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        http.Response.Headers.CacheControl = "private, no-store";
        var nombre = new string(r.NombreArchivo.Select(c => char.IsControl(c) || c is '/' or '\\' or '"' ? '_' : c).ToArray());
        return Results.File(r.Contenido, r.ContentType, string.IsNullOrWhiteSpace(nombre) ? "adjunto" : nombre);
    }
}
