using System.Text;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Facturacion.Application.Comprobantes.Queries;
using Millet.Facturacion.Application.Repp.EmitirRepp;
using Millet.Facturacion.Application.Repp.Queries;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>
/// Endpoints de complementos de pago (REPP) del módulo Facturación (F6).
/// <c>/api/v1/facturacion/repp</c>.
///
/// <para>
/// La automatización (emitir el REPP al confirmarse un cobro vía
/// <c>pago-cliente.confirmado.v1</c> de Tesorería) quedó cableada en
/// <c>TesoreriaEventListenerWorker</c> — cerró
/// PLATFORM-TODO(&lt;PagoClienteConfirmado&gt;) (TES-PR7 + PR gemelo). Este
/// endpoint manual sigue vivo como fallback: cobros sin propuesta CxC,
/// eventos pre-extensión sin desglose de facturas y errores de negocio del
/// flujo automático (marcados en <c>facturacion.evento_procesado</c>).
/// </para>
/// </summary>
public static class ReppEndpoints
{
    public static IEndpointRouteBuilder MapFacturacionReppEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/facturacion/repp")
            .WithTags("Facturacion");

        // POST: emite (sella + timbra) un REPP que cubre una o varias facturas PPD.
        group.MapPost("/", async (
            EmitirReppCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/facturacion/repp/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionReppEmitir)
        .WithName("EmitirRepp")
        .WithSummary("Emite un complemento de pago (REPP / Pago 2.0)")
        .Produces<EmitirReppResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasLeer;

        // GET: bandeja de REPP. B9. CAJAS-PR2: filtrada por la Capa A;
        // ?alcance=sin-asignar restringe al bucket (solo caja.leer-todas).
        group.MapGet("/", async (
            [FromQuery] EstadoTimbrado? estado, [FromQuery] int? offset, [FromQuery] int? limit,
            [FromQuery] string? alcance, IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new BandejaReppQuery(estado, offset ?? 0, limit ?? 50, AlcanceParam.SoloSinAsignar(alcance)), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(leer)
        .WithName("BandejaRepp")
        .WithSummary("Bandeja de complementos de pago (REPP)")
        .Produces<BandejaReppResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // GET: detalle de un REPP con sus facturas cubiertas. B9.
        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var detalle = await mediator.Send(new ReppDetalleQuery(id), ct);
            return Results.Ok(detalle);
        })
        .RequireAuthorization(leer)
        .WithName("ReppDetalle")
        .WithSummary("Detalle de un REPP (facturas cubiertas)")
        .Produces<ReppDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // GET: descarga del XML timbrado del REPP (complemento de pago). P10 —
        // generaliza la descarga de XML a la familia ReciboPago (la query ya la
        // soporta); necesario para observar el Pago 2.0 (EquivalenciaDR /
        // ImpuestosDR) en el manual de correlación.
        group.MapGet("/{id:guid}/xml", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new ComprobanteXmlQuery(id, FamiliaComprobante.ReciboPago), ct);
            return Results.File(Encoding.UTF8.GetBytes(r.Xml), "application/xml", r.NombreArchivo);
        })
        .RequireAuthorization(leer)
        .WithName("ReppXml")
        .WithSummary("Descarga el XML timbrado de un REPP (complemento de pago)")
        .Produces(StatusCodes.Status200OK, contentType: "application/xml")
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // GET: facturas PPD con saldo por cobrar (candidatas de un REPP).
        // Alimenta el FacturaPpdPicker del form de emisión — cierra
        // PLATFORM-TODO(<FacturaPicker>). Mismo permiso que emitir.
        group.MapGet("/facturas-cobrables", async (
            [FromQuery] string? search, [FromQuery] string? receptorRfc, [FromQuery] int? limite,
            IMediator mediator, CancellationToken ct) =>
        {
            var items = await mediator.Send(
                new FacturasCobrablesPpdQuery(search, receptorRfc, limite ?? 100), ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionReppEmitir)
        .WithName("FacturasCobrablesPpd")
        .WithSummary("Facturas PPD con saldo por cobrar (candidatas de un REPP)")
        .Produces<IReadOnlyList<FacturaCobrablePpdItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
