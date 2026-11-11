using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Facturacion.Application.Ingesta.Queries;
using Millet.Facturacion.Application.Ingesta.ResolverExcepcionImportacion;
using Millet.Facturacion.Application.Pedidos.CrearPedidoFacturableManual;
using Millet.Facturacion.Application.Pedidos.EditarPedidoFacturableManual;
using Millet.Facturacion.Application.Pedidos.RegistrarSalidaFacturable;
using Millet.Facturacion.Application.Trazabilidad;
using Millet.Facturacion.Application.Pedidos.Queries;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Application.Proformas;
using Millet.Facturacion.Application.Proformas.CompararProformaPedido;
using Millet.Facturacion.Application.Proformas.EmitirFacturaDesdeProforma;
using Millet.Facturacion.Application.Proformas.GenerarProformaPedido;
using Millet.Facturacion.Application.Proformas.Queries;
using Millet.Facturacion.Application.Proformas.RegistrarConformidadProforma;
using Millet.Facturacion.Domain.Proformas;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>
/// Endpoints de pedidos facturables (F1-PR2). <c>/api/v1/facturacion/pedidos-facturables</c>.
/// Captura manual + bandeja. La ingesta A+W/Planta Pintura + resolución de
/// excepciones entran en F3.
/// </summary>
public static class PedidosFacturablesEndpoints
{
    public static IEndpointRouteBuilder MapFacturacionPedidosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/facturacion/pedidos-facturables")
            .WithTags("Facturacion");

        // GET: bandeja de pedidos facturables. CAJAS-PR2: filtrada por la Capa A;
        // ?alcance=sin-asignar restringe al bucket (solo caja.leer-todas).
        group.MapGet("/", async (
            [FromQuery] EstadoPedidoFacturable? estado,
            [FromQuery] OrigenPedido? origen,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            [FromQuery] string? alcance,
            [FromQuery] ClasePedidoManual? clase,
            [FromQuery] bool? credito,
            [FromQuery] string? q,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            // F6.1: ?clase=VentaDirecta|Emergencia filtra los pedidos manuales clasificados.
            // 86e3tfdw2: ?credito=true|false filtra por condición de pago.
            var response = await mediator.Send(
                new BandejaPedidosFacturablesQuery(
                    estado, origen, offset ?? 0, limit ?? 50, AlcanceParam.SoloSinAsignar(alcance), clase, credito, q),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasLeer)
        .WithName("BandejaPedidosFacturables")
        .WithSummary("Bandeja de pedidos facturables")
        .Produces<BandejaPedidosFacturablesResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // GET: detalle del pedido con líneas + versión (ETag para If-Match). B1.
        group.MapGet("/{id:guid}", async (
            Guid id,
            Guid? salidaId,
            IMediator mediator,
            HttpResponse response,
            CancellationToken cancellationToken) =>
        {
            var detalle = await mediator.Send(new PedidoFacturableDetalleQuery(id, salidaId), cancellationToken);
            response.Headers.ETag = $"\"{detalle.Version.ToString(CultureInfo.InvariantCulture)}\"";
            return Results.Ok(detalle);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasLeer)
        .WithName("PedidoFacturableDetalle")
        .WithSummary("Detalle de un pedido facturable (líneas + versión)")
        .Produces<PedidoFacturableDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // GET: historial de comprobantes emitidos contra el pedido. B5.
        group.MapGet("/{id:guid}/comprobantes", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var items = await mediator.Send(new PedidoComprobantesQuery(id), cancellationToken);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasLeer)
        .WithName("PedidoComprobantes")
        .WithSummary("Historial de comprobantes de un pedido (cancelados + vigente)")
        .Produces<IReadOnlyList<PedidoComprobanteItem>>(StatusCodes.Status200OK);

        // FAC-08: salidas parciales del pedido con su factura vigente y el avance por posición.
        group.MapGet("/{id:guid}/salidas", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
            Results.Ok(await mediator.Send(new PedidoSalidasQuery(id), cancellationToken)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasLeer)
        .WithName("PedidoSalidas")
        .WithSummary("Salidas parciales del pedido, su factura vigente y el avance (FAC-08)")
        .Produces<PedidoSalidasResponse>(StatusCodes.Status200OK);

        // FAC-08: registra una salida parcial a mano (respaldo). Los contenedores de CE llegan solos por evento; PT-03 aún no la publica.
        // Reenviar la misma referencia regresa la salida ya registrada (200) sin cambiar importes.
        group.MapPost("/{id:guid}/salidas", async (
            Guid id,
            RegistrarSalidaRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var r = await mediator.Send(new RegistrarSalidaFacturableCommand(
                id, null, request.Origen ?? OrigenSalidaFacturable.Manual, request.Referencia, request.Lineas), cancellationToken);
            return r.YaRegistrada
                ? Results.Ok(r)
                : Results.Created($"/api/v1/facturacion/pedidos-facturables/{id}/salidas", r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionPedidosCapturar)
        .WithName("RegistrarSalidaFacturable")
        .WithSummary("Registra una salida parcial del pedido para facturarla por separado (FAC-08)")
        .Produces<SalidaFacturableResponse>(StatusCodes.Status201Created)
        .Produces<SalidaFacturableResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // POST: captura manual de un pedido.
        group.MapPost("/", async (
            CrearPedidoFacturableManualCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/facturacion/pedidos-facturables/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionPedidosCapturar)
        .WithName("CrearPedidoFacturableManual")
        .WithSummary("Captura un pedido facturable manual (venta directa o emergencia) con líneas inline")
        .Produces<PedidoFacturableResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // PUT: edición con concurrencia optimista (If-Match con la versión actual).
        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            EditarPedidoFacturableRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseVersion(ifMatch, out var version))
            {
                return Results.Problem(
                    title: "If-Match requerido",
                    detail: "PUT requiere el header If-Match con la versión actual del pedido (ETag, ADR-0012).",
                    statusCode: StatusCodes.Status428PreconditionRequired);
            }

            var response = await mediator.Send(
                new EditarPedidoFacturableManualCommand(
                    id, version, body.ClienteId, body.ClienteNombre, body.CanalVenta,
                    body.ComportamientoFiscal, body.Moneda, body.ObraId, body.ObraNombre,
                    body.Comentarios, body.Lineas, body.ClaseManual, body.SubtipoVentaDirecta,
                    body.MotivoEmergencia, body.ReferenciaPedidoAw),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionPedidosCapturar)
        .WithName("EditarPedidoFacturableManual")
        .WithSummary("Edita un pedido manual no facturado (If-Match → 409 al choque)")
        .Produces<PedidoFacturableResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        // GET: bandeja de excepciones de importación (F3-PR1b).
        group.MapGet("/excepciones", async (
            [FromQuery] bool? soloPendientes,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            [FromQuery] string? q,
            [FromQuery] bool? soloResueltas,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new BandejaExcepcionesImportacionQuery(soloPendientes ?? true, offset ?? 0, limit ?? 50, q, soloResueltas == true),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasLeer)
        .WithName("BandejaExcepcionesImportacion")
        .WithSummary("Bandeja de excepciones de importación A+W/Planta Pintura")
        .Produces<BandejaExcepcionesImportacionResponse>(StatusCodes.Status200OK);

        // POST: resuelve una excepción de importación (F3-PR1b).
        group.MapPost("/excepciones/{id:guid}/resolver", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new ResolverExcepcionImportacionCommand(id), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionPedidosExcepcionesResolver)
        .WithName("ResolverExcepcionImportacion")
        .WithSummary("Marca una excepción de importación como resuelta")
        .Produces<ResolverExcepcionImportacionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // GET: árbol de trazabilidad con el pedido como raíz (doc 13, 13-D):
        // los comprobantes que nacieron de él (FV / anticipos / cartas porte).
        group.MapGet("/{id:guid}/arbol-documentos", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ArbolDocumentosFacturacionQuery(
                    TipoNodoTrazabilidadFacturacion.PedidoFacturable, id),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasLeer)
        .WithName("ArbolDocumentosPedido")
        .WithSummary("Árbol de trazabilidad de un pedido facturable")
        .Produces<ArbolDocumentosFacturacionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // U1.8a: proforma del pedido antes del timbrado. POST genera una versión
        // nueva (sin folio fiscal); mutación → Idempotency-Key (ADR-0020).
        group.MapPost("/{id:guid}/proformas", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new GenerarProformaPedidoCommand(id), cancellationToken);
            return Results.Created($"/api/v1/facturacion/pedidos-facturables/{id}/proformas/ultima", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasEmitir)
        .WithName("GenerarProformaPedido")
        .WithSummary("Genera la proforma (sin valor fiscal) de un pedido por facturar")
        .Produces<ProformaPedidoResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // U1.8a: última proforma del pedido (204 si aún no tiene; 404 si el pedido no existe).
        group.MapGet("/{id:guid}/proformas/ultima", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new UltimaProformaPedidoQuery(id), cancellationToken);
            return response is null ? Results.NoContent() : Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasLeer)
        .WithName("UltimaProformaPedido")
        .WithSummary("Última proforma generada de un pedido")
        .Produces<ProformaPedidoResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // U1.8a: compara el payload de emisión contra la última proforma antes de
        // timbrar. Lectura pura (no persiste) → sin Idempotency-Key.
        group.MapPost("/{id:guid}/proformas/comparar", async (
            Guid id,
            EmitirFacturaVentaCommand emision,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new CompararProformaPedidoQuery(id, emision), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasEmitir)
        .WithName("CompararProformaPedido")
        .WithSummary("Diferencias entre lo que se va a timbrar y la última proforma del pedido")
        .Produces<ComparacionProformaResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // M-234: conformidad del cliente con UNA versión de la proforma (quién, cuándo, medio).
        // Una proforma vencida no se aprueba (422); mutación → Idempotency-Key (ADR-0020).
        group.MapPost("/{id:guid}/proformas/{proformaId:guid}/conformidad", async (
            Guid id,
            Guid proformaId,
            RegistrarConformidadBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new RegistrarConformidadProformaCommand(id, proformaId, body.Quien, body.Medio, body.Fecha, body.Notas),
                cancellationToken);
            return Results.Created($"/api/v1/facturacion/pedidos-facturables/{id}/proformas/ultima", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasEmitir)
        .WithName("RegistrarConformidadProforma")
        .WithSummary("Registra la conformidad del cliente con una versión de la proforma")
        .Produces<ProformaConformidadResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // M-340: factura desde la versión aprobada de la proforma, sin recaptura. Se bloquea sin conformidad o si el pedido
        // cambió después de aprobar (la conformidad queda invalidada); mutación → Idempotency-Key (ADR-0020).
        group.MapPost("/{id:guid}/proformas/{proformaId:guid}/facturar", async (
            Guid id,
            Guid proformaId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new EmitirFacturaDesdeProformaCommand(id, proformaId), cancellationToken);
            return Results.Created($"/api/v1/facturacion/facturas/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasEmitir)
        .WithName("EmitirFacturaDesdeProforma")
        .WithSummary("Emite la factura del pedido desde la versión aprobada de su proforma")
        .Produces<Millet.Facturacion.Application.Facturas.EmitirFacturaVenta.EmitirFacturaVentaResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static bool TryParseVersion(string? ifMatch, out int version)
    {
        version = 0;
        if (string.IsNullOrWhiteSpace(ifMatch)) return false;
        var s = ifMatch.Trim();
        if (s.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        s = s.Trim('"');
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out version);
    }

    /// <summary>Body de edición (la versión va en el header If-Match, no en el cuerpo).
    /// <c>CanalVenta</c> es el id del catálogo <c>compartido.canales_venta</c>
    /// (FAC-ING-PR2) — mismo nombre y forma numérica en JSON que cuando era enum.
    /// F6.1: <c>ClaseManual</c> (1 VentaDirecta, 2 Emergencia) y
    /// <c>SubtipoVentaDirecta</c> (1 Activo, 2 Almacenable, 3 Servicio) van en
    /// forma numérica, igual que <c>ComportamientoFiscal</c>.</summary>
    public sealed record RegistrarSalidaRequest(
        string Referencia,
        IReadOnlyList<SalidaFacturableLineaDto> Lineas,
        OrigenSalidaFacturable? Origen = null);

    /// <summary>M-234: body de conformidad; <c>Medio</c> viaja como texto (Correo, Telefono, Presencial, Mensajeria, Otro).</summary>
    public sealed record RegistrarConformidadBody(
        string Quien,
        [property: System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<MedioConformidadProforma>))] MedioConformidadProforma Medio,
        DateOnly? Fecha = null,
        string? Notas = null);

    public sealed record EditarPedidoFacturableRequest(
        Guid ClienteId,
        string ClienteNombre,
        short CanalVenta,
        ComportamientoFiscal ComportamientoFiscal,
        string Moneda,
        long? ObraId,
        string? ObraNombre,
        string? Comentarios,
        IReadOnlyList<PedidoFacturableLineaInput> Lineas,
        ClasePedidoManual? ClaseManual = null,
        SubtipoVentaDirecta? SubtipoVentaDirecta = null,
        string? MotivoEmergencia = null,
        string? ReferenciaPedidoAw = null);
}
