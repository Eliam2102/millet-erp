using System.Text;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Facturacion.Application.Anticipos.EmitirFacturaAnticipo;
using Millet.Facturacion.Application.Comprobantes.Queries;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Application.Anticipos.Queries;
using Millet.Facturacion.Application.Anticipos.VincularAnticipo;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Application.Reportes;
using Millet.Facturacion.Application.Reportes.ControlAnticipos;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>
/// Endpoints de anticipos del módulo Facturación (F4-PR1): emisión del CFDI de
/// anticipo (serie FANT), vinculación a la factura final (relación 07) y el
/// reporte Control de Anticipos. <c>/api/v1/facturacion/anticipos</c>.
/// </summary>
public static class AnticiposEndpoints
{
    public static IEndpointRouteBuilder MapFacturacionAnticiposEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/facturacion/anticipos")
            .WithTags("Facturacion");

        // POST: emite (sella + timbra) una factura de anticipo y abre su saldo.
        group.MapPost("/", async (
            EmitirFacturaAnticipoCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/facturacion/anticipos/{response.AnticipoId}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionAnticiposEmitir)
        .WithName("EmitirFacturaAnticipo")
        .WithSummary("Emite una factura de anticipo CFDI (serie FANT) y abre su saldo")
        .Produces<EmitirFacturaAnticipoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // POST: vincula el anticipo {id} a una factura final (M2, relación 07).
        group.MapPost("/{id:guid}/vincular", async (
            Guid id,
            VincularAnticipoRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new VincularAnticipoCommand(id, body.FacturaVentaId, body.Importe), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionAnticiposVincular)
        .WithName("VincularAnticipo")
        .WithSummary("Vincula un anticipo a una factura final (relación 07)")
        .Produces<VincularAnticipoResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // GET: bandeja de facturas de anticipo (CAJAS-PR2, filtrada por la Capa A;
        // ?alcance=sin-asignar restringe al bucket, solo caja.leer-todas).
        group.MapGet("/facturas", async (
            [FromQuery] EstadoTimbrado? estado,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            [FromQuery] string? alcance,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new BandejaFacturasAnticipoQuery(
                    estado, offset ?? 0, limit ?? 50, AlcanceParam.SoloSinAsignar(alcance)),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionAnticiposLeer)
        .WithName("BandejaFacturasAnticipo")
        .WithSummary("Bandeja de facturas de anticipo")
        .Produces<BandejaFacturasAnticipoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // GET: detalle de una factura de anticipo (CAJAS-PR2; fuera de alcance → 404).
        group.MapGet("/facturas/{id:guid}", async (
            Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var detalle = await mediator.Send(new FacturaAnticipoDetalleQuery(id), cancellationToken);
            return Results.Ok(detalle);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionAnticiposLeer)
        .WithName("FacturaAnticipoDetalle")
        .WithSummary("Detalle de una factura de anticipo")
        .Produces<FacturaAnticipoDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // GET: descarga del XML timbrado de la factura de anticipo (doc 13 §5).
        group.MapGet("/facturas/{id:guid}/xml", async (
            Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var r = await mediator.Send(
                new ComprobanteXmlQuery(id, FamiliaComprobante.FacturaAnticipo), cancellationToken);
            return Results.File(Encoding.UTF8.GetBytes(r.Xml), "application/xml", r.NombreArchivo);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionAnticiposLeer)
        .WithName("FacturaAnticipoXml")
        .WithSummary("Descarga el XML timbrado de una factura de anticipo")
        .Produces(StatusCodes.Status200OK, contentType: "application/xml")
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // GET: PDF bilingüe de la factura de anticipo (concepto único, 13-C).
        group.MapGet("/facturas/{id:guid}/pdf", async (
            Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var pdf = await mediator.Send(
                new ComprobantePdfQuery(id, FormatoPdfFactura.Bilingue, FamiliaComprobante.FacturaAnticipo),
                cancellationToken);
            return Results.File(pdf.Contenido, pdf.ContentType, pdf.NombreSugerido);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionAnticiposLeer)
        .WithName("FacturaAnticipoPdf")
        .WithSummary("Genera el PDF bilingüe de una factura de anticipo")
        .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
        .ProducesProblem(StatusCodes.Status404NotFound);

        // GET: reporte Control de Anticipos — vista Resumen (§6.7, contrato ADR-0036).
        group.MapGet("/control", async (
            [FromQuery] Guid? clienteId,
            [FromQuery] EstadoAnticipo? estado,
            [FromQuery] long? obraId,
            [FromQuery] DateTimeOffset? desde,
            [FromQuery] DateTimeOffset? hasta,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var reporte = await mediator.Send(
                new ControlAnticiposResumenQuery(clienteId, estado, obraId, desde, hasta), cancellationToken);
            return Results.Ok(reporte);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionAnticiposLeer)
        .WithName("ControlAnticiposResumen")
        .WithSummary("Reporte Control de Anticipos (saldos amortizables por cliente)")
        .Produces<ReporteResponse<ControlAnticipoFila>>(StatusCodes.Status200OK);

        // GET: Control de Anticipos — vista Detallada (estado de cuenta por cliente). B12.
        group.MapGet("/control/{clienteId:guid}", async (
            Guid clienteId, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var detalle = await mediator.Send(new ControlAnticiposDetalladaQuery(clienteId), cancellationToken);
            return Results.Ok(detalle);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionAnticiposLeer)
        .WithName("ControlAnticiposDetallada")
        .WithSummary("Control de Anticipos detallado (estado de cuenta por cliente)")
        .Produces<ControlAnticiposDetalladaResponse>(StatusCodes.Status200OK);

        return app;
    }

    /// <summary>Body de la vinculación de anticipo a factura final.</summary>
    public sealed record VincularAnticipoRequest(Guid FacturaVentaId, decimal Importe);
}
