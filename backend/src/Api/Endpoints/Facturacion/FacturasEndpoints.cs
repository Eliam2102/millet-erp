using System.Text;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Facturacion.Application.Facturas.AplicarPedimento;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Application.Comprobantes.Queries;
using Millet.Facturacion.Application.Facturas.Queries;
using Millet.Facturacion.Application.Facturas.ReenviarCfdiCorreo;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>
/// Endpoints de facturas de venta del módulo Facturación. POST emisión (F1-PR1);
/// bandeja + detalle (F1-PR2). <c>/api/v1/facturacion/facturas</c>.
/// </summary>
public static class FacturasEndpoints
{
    public static IEndpointRouteBuilder MapFacturacionFacturasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/facturacion/facturas")
            .WithTags("Facturacion");

        // POST: emite (sella + timbra) una factura de venta. Mutación con
        // impacto fiscal → Idempotency-Key obligatorio (ADR-0020).
        group.MapPost("/", async (
            EmitirFacturaVentaCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/facturacion/facturas/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasEmitir)
        .WithName("EmitirFacturaVenta")
        .WithSummary("Emite (sella + timbra) una factura de venta CFDI")
        .Produces<EmitirFacturaVentaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // GET: bandeja de facturas (F1-PR2). CAJAS-PR2: filtrada por la Capa A;
        // ?alcance=sin-asignar restringe al bucket (solo caja.leer-todas).
        group.MapGet("/", async (
            [FromQuery] EstadoTimbrado? estado,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            [FromQuery] string? alcance,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new BandejaFacturasQuery(estado, offset ?? 0, limit ?? 50, AlcanceParam.SoloSinAsignar(alcance)),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasLeer)
        .WithName("BandejaFacturas")
        .WithSummary("Bandeja de facturas de venta")
        .Produces<BandejaFacturasResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // GET: detalle de una factura + líneas + datos del timbre (F1-PR2).
        group.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var detalle = await mediator.Send(new ComprobanteDetalleQuery(id), cancellationToken);
            return Results.Ok(detalle);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasLeer)
        .WithName("ComprobanteDetalle")
        .WithSummary("Detalle de una factura de venta (líneas + timbre + relaciones)")
        .Produces<ComprobanteDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // GET: bitácora de envíos de correo de la factura. B6.
        group.MapGet("/{id:guid}/envios", async (
            Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var items = await mediator.Send(new EnviosFacturaQuery(id), cancellationToken);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasLeer)
        .WithName("EnviosFactura")
        .WithSummary("Bitácora de envíos por correo de una factura")
        .Produces<IReadOnlyList<EnvioCorreoItem>>(StatusCodes.Status200OK);

        // GET: descarga del XML timbrado. B7.
        group.MapGet("/{id:guid}/xml", async (
            Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var r = await mediator.Send(
                new ComprobanteXmlQuery(id, FamiliaComprobante.FacturaVenta), cancellationToken);
            return Results.File(Encoding.UTF8.GetBytes(r.Xml), "application/xml", r.NombreArchivo);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasLeer)
        .WithName("FacturaXml")
        .WithSummary("Descarga el XML timbrado de una factura")
        .Produces(StatusCodes.Status200OK, contentType: "application/xml")
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // GET: PDF (formato=completo|termica). B8.
        group.MapGet("/{id:guid}/pdf", async (
            Guid id, [FromQuery] string? formato, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var fmt = string.Equals(formato, "termica", StringComparison.OrdinalIgnoreCase)
                ? FormatoPdfFactura.TermicaSimplificada
                : FormatoPdfFactura.Bilingue;
            var pdf = await mediator.Send(
                new ComprobantePdfQuery(id, fmt, FamiliaComprobante.FacturaVenta), cancellationToken);
            return Results.File(pdf.Contenido, pdf.ContentType, pdf.NombreSugerido);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasLeer)
        .WithName("FacturaPdf")
        .WithSummary("Genera el PDF de una factura (completo bilingüe o térmica)")
        .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
        .ProducesProblem(StatusCodes.Status404NotFound);

        // POST: aplica el pedimento a una factura retenida y dispara su timbre. F7-PR2.
        group.MapPost("/{id:guid}/pedimento", async (
            Guid id,
            AplicarPedimentoRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new AplicarPedimentoCommand(id, body.Pedimento, body.FechaDocAduanero, body.IdentificacionMercancia),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasEmitir)
        .WithName("AplicarPedimento")
        .WithSummary("Aplica el pedimento a una factura retenida y la timbra")
        .Produces<AplicarPedimentoResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // POST: reenvía el CFDI por correo (encola; el worker lo entrega). F2-PR2.
        group.MapPost("/{id:guid}/reenviar-correo", async (
            Guid id,
            ReenviarCfdiCorreoRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ReenviarCfdiCorreoCommand(id, body.Destinatario), cancellationToken);
            return Results.Accepted($"/api/v1/facturacion/facturas/{id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasEmitir)
        .WithName("ReenviarCfdiCorreo")
        .WithSummary("Reenvía el CFDI de una factura por correo al cliente")
        .Produces<ReenviarCfdiCorreoResponse>(StatusCodes.Status202Accepted)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    /// <summary>Body del reenvío de CFDI por correo.</summary>
    public sealed record ReenviarCfdiCorreoRequest(string Destinatario);

    /// <summary>Body de la aplicación de pedimento.</summary>
    public sealed record AplicarPedimentoRequest(string Pedimento, DateOnly? FechaDocAduanero, string? IdentificacionMercancia);
}
