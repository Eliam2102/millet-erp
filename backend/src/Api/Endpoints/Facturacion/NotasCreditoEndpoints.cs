using System.Text;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Facturacion.Application.Comprobantes.Queries;
using Millet.Facturacion.Application.NotasCredito.EmitirNotaCreditoBonificacion;
using Millet.Facturacion.Application.NotasCredito.Queries;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>
/// Endpoints de notas de crédito del módulo Facturación. F5-PR1: emisión por
/// bonificación (relación 01). <c>/api/v1/facturacion/notas-credito</c>.
/// </summary>
public static class NotasCreditoEndpoints
{
    public static IEndpointRouteBuilder MapFacturacionNotasCreditoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/facturacion/notas-credito")
            .WithTags("Facturacion");

        // POST: emite (sella + timbra) una NC por bonificación sobre una factura.
        group.MapPost("/bonificacion", async (
            EmitirNotaCreditoBonificacionCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/facturacion/notas-credito/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionNotasCreditoBonificacion)
        .WithName("EmitirNotaCreditoBonificacion")
        .WithSummary("Emite una NC por bonificación sobre una factura (relación 01)")
        .Produces<EmitirNotaCreditoBonificacionResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // GET: bandeja de notas de crédito (CAJAS-PR2, filtrada por la Capa A;
        // ?alcance=sin-asignar restringe al bucket, solo caja.leer-todas).
        group.MapGet("/", async (
            [FromQuery] EstadoTimbrado? estado,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            [FromQuery] string? alcance,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new BandejaNotasCreditoQuery(
                    estado, offset ?? 0, limit ?? 50, AlcanceParam.SoloSinAsignar(alcance)),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionNotasCreditoLeer)
        .WithName("BandejaNotasCredito")
        .WithSummary("Bandeja de notas de crédito")
        .Produces<BandejaNotasCreditoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // GET: detalle de una NC (CAJAS-PR2; fuera de alcance → 404).
        group.MapGet("/{id:guid}", async (
            Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var detalle = await mediator.Send(new NotaCreditoDetalleQuery(id), cancellationToken);
            return Results.Ok(detalle);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionNotasCreditoLeer)
        .WithName("NotaCreditoDetalle")
        .WithSummary("Detalle de una nota de crédito")
        .Produces<NotaCreditoDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // GET: descarga del XML timbrado de la NC (doc 13 §5).
        group.MapGet("/{id:guid}/xml", async (
            Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var r = await mediator.Send(
                new ComprobanteXmlQuery(id, FamiliaComprobante.NotaCredito), cancellationToken);
            return Results.File(Encoding.UTF8.GetBytes(r.Xml), "application/xml", r.NombreArchivo);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionNotasCreditoLeer)
        .WithName("NotaCreditoXml")
        .WithSummary("Descarga el XML timbrado de una nota de crédito")
        .Produces(StatusCodes.Status200OK, contentType: "application/xml")
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // GET: PDF bilingüe de la NC (Egreso: motivo + relaciones, 13-C).
        group.MapGet("/{id:guid}/pdf", async (
            Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var pdf = await mediator.Send(
                new ComprobantePdfQuery(id, FormatoPdfFactura.Bilingue, FamiliaComprobante.NotaCredito),
                cancellationToken);
            return Results.File(pdf.Contenido, pdf.ContentType, pdf.NombreSugerido);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionNotasCreditoLeer)
        .WithName("NotaCreditoPdf")
        .WithSummary("Genera el PDF bilingüe de una nota de crédito")
        .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
