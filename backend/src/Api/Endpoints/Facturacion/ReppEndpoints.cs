using System.Text;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Facturacion.Application.Comprobantes.Queries;
using Millet.Facturacion.Application.Repp.EmitirRepp;
using Millet.Facturacion.Application.Repp.Queries;
using Millet.Facturacion.Application.Repp.Pendientes;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>Emisión manual de REP y bandeja de pagos bancarios confirmados pendientes de revisión.</summary>
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

        var pendientes = group.MapGroup("/pendientes");
        var emitir = PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionReppEmitir;
        pendientes.MapGet("/", async ([FromQuery] EstadoReppPendiente? estado, [FromQuery] string? indicador,
            [FromQuery] int? offset, [FromQuery] int? limit, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ReppPendientesQuery(estado, indicador, offset ?? 0, limit ?? 10), ct)))
            .RequireAuthorization(leer).WithName("ListarReppPendientes");
        pendientes.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ReppPendienteQuery(id), ct)))
            .RequireAuthorization(leer).WithName("ConsultarReppPendiente");
        pendientes.MapPut("/{id:guid}", async (Guid id, RevisarReppPendienteBody body, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new RevisarReppPendienteCommand(id, body.FormaPago, body.Facturas), ct);
            return Results.NoContent();
        }).RequireAuthorization(emitir).WithName("RevisarReppPendiente");
        pendientes.MapPost("/{id:guid}/emitir", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new EmitirReppPendienteCommand(id), ct);
            return result.Emitido ? Results.Ok(result) : Results.Problem(
                title: "No se pudo emitir el REP", detail: result.Mensaje,
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?> { ["code"] = result.Codigo, ["pendienteId"] = result.Id, ["reciboPagoId"] = result.ReciboPagoId });
        }).RequireAuthorization(emitir).WithMetadata(new RequireIdempotencyKeyAttribute()).WithName("EmitirReppPendiente");
        pendientes.MapPost("/emitir-lote", async (EmitirReppPendientesLoteCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command, ct)))
            .RequireAuthorization(emitir).WithMetadata(new RequireIdempotencyKeyAttribute()).WithName("EmitirReppPendientesLote");
        pendientes.MapPost("/{id:guid}/descartar", async (Guid id, DescartarReppPendienteBody body, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new DescartarReppPendienteCommand(id, body.Motivo), ct);
            return Results.NoContent();
        }).RequireAuthorization(emitir).WithName("DescartarReppPendiente");
        return app;
    }
}

public sealed record RevisarReppPendienteBody(string FormaPago, IReadOnlyList<RelacionRepp> Facturas);
public sealed record DescartarReppPendienteBody(string Motivo);
