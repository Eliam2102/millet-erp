using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Facturacion.Application.Ingesta.Queries;
using Millet.Facturacion.Application.Ingesta.ResolverExcepcionImportacion;
using Millet.Facturacion.Application.Pedidos.CrearPedidoFacturableManual;
using Millet.Facturacion.Application.Pedidos.EditarPedidoFacturableManual;
using Millet.Facturacion.Application.Trazabilidad;
using Millet.Facturacion.Application.Pedidos.Queries;
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
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new BandejaPedidosFacturablesQuery(
                    estado, origen, offset ?? 0, limit ?? 50, AlcanceParam.SoloSinAsignar(alcance)),
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
            IMediator mediator,
            HttpResponse response,
            CancellationToken cancellationToken) =>
        {
            var detalle = await mediator.Send(new PedidoFacturableDetalleQuery(id), cancellationToken);
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
        .WithSummary("Captura un pedido facturable manual con líneas inline")
        .Produces<PedidoFacturableResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status403Forbidden);

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
                    body.Comentarios, body.Lineas),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionPedidosCapturar)
        .WithName("EditarPedidoFacturableManual")
        .WithSummary("Edita un pedido manual no facturado (If-Match → 409 al choque)")
        .Produces<PedidoFacturableResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        // GET: bandeja de excepciones de importación (F3-PR1b).
        group.MapGet("/excepciones", async (
            [FromQuery] bool? soloPendientes,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var items = await mediator.Send(
                new BandejaExcepcionesImportacionQuery(soloPendientes ?? true, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasLeer)
        .WithName("BandejaExcepcionesImportacion")
        .WithSummary("Bandeja de excepciones de importación A+W/Planta Pintura")
        .Produces<IReadOnlyList<ExcepcionImportacionItem>>(StatusCodes.Status200OK);

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
    /// (FAC-ING-PR2) — mismo nombre y forma numérica en JSON que cuando era enum.</summary>
    public sealed record EditarPedidoFacturableRequest(
        Guid ClienteId,
        string ClienteNombre,
        short CanalVenta,
        ComportamientoFiscal ComportamientoFiscal,
        string Moneda,
        long? ObraId,
        string? ObraNombre,
        string? Comentarios,
        IReadOnlyList<PedidoFacturableLineaInput> Lineas);
}
