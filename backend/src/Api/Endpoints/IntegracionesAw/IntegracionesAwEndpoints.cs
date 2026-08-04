using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Integraciones.Aw.Application.Commands.MarcarResueltoManual;
using Millet.Integraciones.Aw.Application.Commands.RegistrarCotizacionEdi;
using Millet.Integraciones.Aw.Application.Commands.ReintentarCotizacion;
using Millet.Integraciones.Aw.Application.Queries.ListarCotizaciones;
using Millet.Integraciones.Aw.Application.Queries.ObtenerCotizacionDetalle;
using Millet.Integraciones.Aw.Application.Queries.ObtenerCotizacionPdf;
using Millet.Integraciones.Aw.Application.Queries.ObtenerHistorialCotizacion;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Domain.Ports.Blob;

namespace Millet.Api.Endpoints.IntegracionesAw;

/// <summary>
/// Endpoints REST del módulo <c>Millet.Integraciones.Aw</c> (PR D).
/// Ruta base: <c>/api/v1/integraciones/aw/cotizaciones</c>.
///
/// <para>
/// <b>Auth:</b> cada endpoint usa
/// <see cref="PermissionPolicyProvider"/> + <see cref="PermisosCanonicos"/>.
/// Tanto humanos (API JWT) como service principals (Glass Agent vía
/// Bearer Entra) son aceptados — el stack los unifica vía
/// <c>ICurrentUserContext.UserId</c> (PR A).
/// </para>
///
/// <para>
/// <b>Idempotency:</b> POST endpoints declaran
/// <c>.WithMetadata(new RequireIdempotencyKeyAttribute())</c> para
/// activar el <c>IdempotencyMiddleware</c> de SharedKernel.
/// </para>
///
/// <para>
/// <b>Empresa:</b> el global query filter de EF (ADR-0011) restringe
/// automáticamente a la empresa del JWT. Los endpoints NO aceptan
/// empresaId como parámetro — el caller no puede consultar otra empresa.
/// </para>
/// </summary>
public static class IntegracionesAwEndpoints
{
    public static IEndpointRouteBuilder MapIntegracionesAwEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/integraciones/aw/cotizaciones")
            .WithTags("Integraciones AW");

        // POST / — registrar cotización EDI (Glass Agent)
        group.MapPost("/", async (
            [FromBody] RegistrarCotizacionEdiCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.AcceptedAtRoute(
                routeName: "ObtenerCotizacionDetalle",
                routeValues: new { id = response.Id },
                value: response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesAwCotizacionesCrear)
        .WithName("RegistrarCotizacion")
        .WithSummary("Registra una nueva cotización EDI enviada por el Glass Agent.")
        .WithDescription(
            "Persiste la cotización en estado Submitted y emite AwCotizacionRecibida al " +
            "Outbox para que AwDropWorker (PR C) ejecute el drop al on-prem. Idempotency-Key " +
            "requerido. La unicidad de quote_reference por empresa se valida con UNIQUE " +
            "constraint — duplicado retorna 409 aw_quote_reference_duplicada.")
        .Produces<RegistrarCotizacionEdiResponse>(StatusCodes.Status202Accepted)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // GET / — listar cotizaciones (bandeja operadores)
        group.MapGet("/", async (
            IMediator mediator,
            CancellationToken cancellationToken,
            [FromQuery] EstadoEntidad? estado = null,
            [FromQuery] DateTimeOffset? desde = null,
            [FromQuery] DateTimeOffset? hasta = null,
            [FromQuery(Name = "quoteReference")] string? quoteReference = null,
            [FromQuery(Name = "quoteReferenceSearch")] string? quoteReferenceSearch = null,
            [FromQuery(Name = "awDocId")] long? awDocId = null,
            [FromQuery] int offset = 0,
            [FromQuery] int limit = 50) =>
        {
            var query = new ListarCotizacionesQuery(
                estado, desde, hasta, quoteReference, quoteReferenceSearch,
                awDocId, offset, limit);
            var result = await mediator.Send(query, cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesAwCotizacionesConsultar)
        .WithName("ListarCotizaciones")
        .WithSummary("Lista cotizaciones de la empresa actual con filtros opcionales.")
        .WithDescription(
            "Paginación offset-based (default 50, max 200). Empresa se aplica vía global " +
            "query filter — el caller no puede consultar otra empresa. Sort: SubmittedAt DESC.")
        .Produces<PagedResponse<CotizacionResumenItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // GET /{id} — detalle
        group.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var detalle = await mediator.Send(
                new ObtenerCotizacionDetalleQuery(id), cancellationToken);
            return detalle is null
                ? Results.NotFound()
                : Results.Ok(detalle);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesAwCotizacionesConsultar)
        .WithName("ObtenerCotizacionDetalle")
        .WithSummary("Detalle de una cotización (estado, correlación, envíos recientes).")
        .Produces<CotizacionDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // GET /{id}/pdf — descarga el PDF de A+W (oferta/pedido) adjunto
        group.MapGet("/{id:guid}/pdf", async (
            Guid id,
            IMediator mediator,
            IAlmacenarBlobPort blob,
            CancellationToken cancellationToken) =>
        {
            var pdfRef = await mediator.Send(new ObtenerCotizacionPdfQuery(id), cancellationToken);
            if (pdfRef is null) return Results.NotFound();

            var stream = await blob.ObtenerStreamAsync(pdfRef.BlobUrl, cancellationToken);
            return Results.File(stream, "application/pdf", fileDownloadName: pdfRef.Filename);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesAwCotizacionesConsultar)
        .WithName("ObtenerCotizacionPdf")
        .WithSummary("Descarga el PDF de A+W (oferta/pedido) adjunto a la cotización.")
        .WithDescription(
            "Proxy autenticado del blob: el ERP lee el PDF de Blob Storage y lo " +
            "devuelve como application/pdf. El Glass Agent (Bearer JWT) lo consume vía " +
            "el campo pdfUrl del detalle. 404 si la cotización no existe o aún no tiene PDF.")
        .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // GET /{id}/historial
        group.MapGet("/{id:guid}/historial", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var historial = await mediator.Send(
                new ObtenerHistorialCotizacionQuery(id), cancellationToken);
            return historial is null
                ? Results.NotFound()
                : Results.Ok(historial);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesAwCotizacionesConsultar)
        .WithName("ObtenerHistorialCotizacion")
        .WithSummary("Historial cronológico de envíos (drops) de una cotización.")
        .Produces<HistorialResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // POST /{id}/reintentar
        group.MapPost("/{id:guid}/reintentar", async (
            Guid id,
            [FromBody] ReintentarRequestBody? body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ReintentarCotizacionCommand(id, body?.Razon), cancellationToken);
            return Results.Accepted(value: response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesAwCotizacionesReintentar)
        .WithName("ReintentarCotizacion")
        .WithSummary("Reactiva una cotización en FailedDrop o ManuallyResolved.")
        .WithDescription(
            "Resetea retry_count y emite AwCotizacionRecibida al Outbox. Estados no permitidos " +
            "(Submitted, Correlated, FailedCorrelation) retornan 409 " +
            "aw_invalid_state_transition.")
        .Produces<ReintentarCotizacionResponse>(StatusCodes.Status202Accepted)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // POST /{id}/marcar-resuelto
        group.MapPost("/{id:guid}/marcar-resuelto", async (
            Guid id,
            [FromBody] MarcarResueltoRequestBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new MarcarResueltoManualCommand(id, body.Nota), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesAwCotizacionesReintentar)
        .WithName("MarcarResueltoManual")
        .WithSummary("Cierra administrativamente una cotización en FailedDrop o FailedCorrelation.")
        .WithDescription(
            "Marca como ManuallyResolved con nota del operador. Estado activo " +
            "(Submitted) retorna 409 — debe esperar al desenlace natural del drop.")
        .Produces<MarcarResueltoManualResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // ===================================================================
        // FLUJO 2 (ADR-0048, PR7) — nudge de la ingesta de pedidos en firme.
        // NO pertenece al flujo de cotizaciones del Glass Agent de arriba.
        // ===================================================================
        //
        // La customización de A+W, tras INSERTar una solicitud en la
        // tabla-puente (MILLET_INTEGRACION.aw_solicitud_pedido), llama al exe
        // on-prem MilletAwPedidoNotify que hace este POST para que el
        // AwSolicitudesWorker tickee de inmediato en vez de esperar el
        // intervalo de polling. BEST-EFFORT: si el nudge se pierde, el
        // polling normal drena la cola — la tabla es la fuente de verdad.
        //
        // Auth: API key dedicada (header X-Millet-Nudge-Key) comparada en
        // tiempo constante contra IntegracionesAw:Pedidos:NudgeApiKey (app
        // setting/KV). Anónimo a nivel JWT porque el caller es un exe simple
        // sin flujo Entra; el endpoint no recibe datos ni devuelve estado —
        // el peor abuso posible es provocar ticks extra (barrido barato e
        // idempotente). Key sin configurar → endpoint apagado (404).
        app.MapPost("/api/v1/integraciones/aw/pedidos/nudge", (
            HttpRequest request,
            IConfiguration configuration,
            Millet.Facturacion.Infrastructure.Workers.AwSolicitudesTickSignal signal) =>
        {
            var keyConfigurada = configuration["IntegracionesAw:Pedidos:NudgeApiKey"];
            if (string.IsNullOrWhiteSpace(keyConfigurada))
                return Results.NotFound();

            if (!request.Headers.TryGetValue("X-Millet-Nudge-Key", out var keyRecibida)
                || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(keyRecibida.ToString()),
                    System.Text.Encoding.UTF8.GetBytes(keyConfigurada)))
            {
                // Respuesta idéntica a "no existe" — no filtra validez de keys.
                return Results.NotFound();
            }

            signal.Solicitar();
            return Results.Accepted();
        })
        .AllowAnonymous()
        .WithTags("Integraciones AW")
        .WithName("NudgeIngestaPedidosAw")
        .WithSummary("Despierta al worker de ingesta de pedidos A+W (flujo 2, ADR-0048)")
        .Produces(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}

/// <summary>Body opcional del POST reintentar.</summary>
public sealed record ReintentarRequestBody(string? Razon);

/// <summary>Body requerido del POST marcar-resuelto.</summary>
public sealed record MarcarResueltoRequestBody(string Nota);
