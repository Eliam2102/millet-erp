using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorPagar.Application.Cfdi.DescartarCfdi;
using Millet.CuentasPorPagar.Application.Cfdi.IngresarCfdi;
using Millet.CuentasPorPagar.Application.Cfdi.ListarCfdis;
using Millet.CuentasPorPagar.Application.Cfdi.DescargarCfdiBlob;
using Millet.CuentasPorPagar.Application.Cfdi.MarcarCfdiDuplicado;
using Millet.CuentasPorPagar.Application.Cfdi.ObtenerCfdiParseado;
using Millet.CuentasPorPagar.Application.Cfdi.ObtenerCfdiPorId;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.CuentasPorPagar;

/// <summary>
/// Endpoints HTTP del módulo Cuentas por Pagar para el agregado
/// <c>CfdiRecibido</c> (F1-PR1). Cubre:
/// <list type="bullet">
///   <item><c>POST /api/v1/cuentas-por-pagar/cfdis/cargar</c> — carga
///         manual de respaldo (multipart con XML + PDF opcional).</item>
///   <item><c>GET  /api/v1/cuentas-por-pagar/cfdis</c> — bandeja
///         paginada con filtros (estado, tipo, RFC emisor).</item>
///   <item><c>POST /api/v1/cuentas-por-pagar/cfdis/{id}/descartar</c></item>
///   <item><c>POST /api/v1/cuentas-por-pagar/cfdis/{id}/marcar-duplicado</c></item>
/// </list>
/// </summary>
public static class CfdisEndpoints
{
    public static IEndpointRouteBuilder MapCfdisEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/cuentas-por-pagar/cfdis")
            .WithTags("CuentasPorPagar")
            .RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery] EstadoCfdiRecibido? estado,
            [FromQuery] TipoCfdi? tipo,
            [FromQuery] string? rfcEmisor,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarCfdisQuery(
                    Estado: estado,
                    Tipo: tipo,
                    RfcEmisor: rfcEmisor,
                    Offset: offset ?? 0,
                    Limit: limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarCfdisLeer)
        .WithName("ListarCfdisRecibidos")
        .WithSummary("Bandeja paginada de CFDIs recibidos")
        .WithDescription(
            "Lista los CFDIs recibidos con filtros opcionales por estado, " +
            "tipo y RFC emisor. Default: 50 items, max 500. Orden " +
            "`FechaRecepcion DESC`. Permiso `cuentas_por_pagar.cfdis.leer`.")
        .Produces<PagedResponse<CfdiListItemResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ObtenerCfdiPorIdQuery(id),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarCfdisLeer)
        .WithName("ObtenerCfdiPorId")
        .WithSummary("Detalle de un CFDI recibido")
        .WithDescription(
            "Metadata completa del CFDI (importes, canal, estado, motivo " +
            "de descarte, flags de XML/PDF). Permiso " +
            "`cuentas_por_pagar.cfdis.leer`.")
        .Produces<CfdiDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/xml", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var blob = await mediator.Send(
                new DescargarCfdiBlobQuery(id, Pdf: false),
                cancellationToken);
            return Results.Stream(blob.Contenido, blob.ContentType, blob.FileName);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarCfdisLeer)
        .WithName("DescargarCfdiXml")
        .WithSummary("Descarga el XML del CFDI desde el blob storage")
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:guid}/pdf", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var blob = await mediator.Send(
                new DescargarCfdiBlobQuery(id, Pdf: true),
                cancellationToken);
            return Results.Stream(blob.Contenido, blob.ContentType, blob.FileName);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarCfdisLeer)
        .WithName("DescargarCfdiPdf")
        .WithSummary("Descarga la representación impresa (PDF) del CFDI")
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:guid}/parseado", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ObtenerCfdiParseadoQuery(id),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarCfdisLeer)
        .WithName("ObtenerCfdiParseado")
        .WithSummary("Re-parsea el XML del CFDI para pre-llenar la captura de factura")
        .WithDescription(
            "Lee el XML del blob storage y devuelve encabezado (subtotal, " +
            "impuestos, retenciones, TC) + líneas de cfdi:Concepto. " +
            "Permiso `cuentas_por_pagar.cfdis.leer`.")
        .Produces<CfdiParseadoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/cargar", async (
            IFormFile xml,
            IFormFile? pdf,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (xml is null || xml.Length == 0)
            {
                throw new BusinessRuleException(
                    "CFDI_XML_REQUERIDO",
                    "Debe enviarse el XML del CFDI en el campo 'xml' del multipart.");
            }

            await using var xmlStream = xml.OpenReadStream();
            Stream? pdfStream = null;
            if (pdf is { Length: > 0 })
            {
                pdfStream = pdf.OpenReadStream();
            }

            try
            {
                var response = await mediator.Send(
                    new IngresarCfdiCommand(
                        Xml: xmlStream,
                        Pdf: pdfStream,
                        Canal: CanalOrigenCfdi.CargaManual),
                    cancellationToken);

                return Results.Created($"/api/v1/cuentas-por-pagar/cfdis/{response.Id}", response);
            }
            finally
            {
                if (pdfStream is not null) await pdfStream.DisposeAsync();
            }
        })
        .DisableAntiforgery()
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarCfdisCargarManual)
        .WithName("CargarCfdiManual")
        .WithSummary("Cargar un CFDI manualmente (multipart/form-data, canal de respaldo)")
        .WithDescription(
            "Carga manual de un CFDI 4.0 cuando los canales automáticos " +
            "(descarga SAT, mailbox) no aplican. Campos del multipart: " +
            "`xml` (IFormFile, obligatorio), `pdf` (IFormFile, opcional). " +
            "Permiso `cuentas_por_pagar.cfdis.cargar-manual` + " +
            "`Idempotency-Key` obligatorio.")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<IngresarCfdiResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/descartar", async (
            Guid id,
            [FromBody] DescartarCfdiRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            await mediator.Send(new DescartarCfdiCommand(id, request.Motivo), cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarCfdisDescartar)
        .WithName("DescartarCfdi")
        .WithSummary("Descartar un CFDI en estado PorProcesar")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/marcar-duplicado", async (
            Guid id,
            [FromBody] MarcarDuplicadoRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            await mediator.Send(
                new MarcarCfdiDuplicadoCommand(id, request.CfdiOriginalId),
                cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarCfdisDescartar)
        .WithName("MarcarCfdiDuplicado")
        .WithSummary("Marcar un CFDI como duplicado de otro previamente ingresado")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    public sealed record DescartarCfdiRequest(string Motivo);
    public sealed record MarcarDuplicadoRequest(Guid CfdiOriginalId);
}
