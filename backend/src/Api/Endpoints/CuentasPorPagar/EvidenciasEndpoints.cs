using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorPagar.Application.Evidencias.AdjuntarEvidencia;
using Millet.CuentasPorPagar.Application.Evidencias.Queries;
using Millet.CuentasPorPagar.Domain.Evidencias;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.CuentasPorPagar;

/// <summary>
/// Endpoints de evidencias polimórficas (F4-PR2). MVP solo cubre
/// <c>FacturaProveedor</c>; los otros 3 tipos quedan reservados.
/// Sigue el patrón de Compras OC adjuntos: el endpoint sube el blob
/// PRIMERO y luego invoca el comando con la ref.
/// </summary>
public static class EvidenciasEndpoints
{
    public static IEndpointRouteBuilder MapEvidenciasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/cuentas-por-pagar/facturas/{facturaId:guid}/evidencias")
            .WithTags("CuentasPorPagar")
            .RequireAuthorization();

        group.MapGet("/", async (
            Guid facturaId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarEvidenciasQuery(TipoDocumentoEvidencia.FacturaProveedor, facturaId),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarFacturasLeer)
        .WithName("ListarEvidenciasFactura")
        .Produces<IReadOnlyList<EvidenciaResponse>>(StatusCodes.Status200OK);

        group.MapPost("/", async (
            Guid facturaId,
            [FromForm] TipoEvidencia tipo,
            [FromForm] string comentario,
            [FromForm] EstadoFirmaFisica estadoFirmaFisica,
            [FromForm] DateOnly? fechaLimiteFirmaFisica,
            IFormFile archivo,
            IMediator mediator,
            IEvidenciaBlobStorage blob,
            Millet.SharedKernel.Application.IClock clock,
            CancellationToken cancellationToken) =>
        {
            if (archivo is null || archivo.Length == 0)
            {
                throw new BusinessRuleException(
                    "ARCHIVO_REQUERIDO",
                    "Debe enviarse un archivo en el campo 'archivo' del multipart.");
            }

            // Sube blob primero. Si la persistencia de metadata falla luego
            // queda un blob huérfano que un proceso de limpieza recoge
            // (post-MVP). Igual patrón que Compras OC adjuntos.
            var evidenciaId = Guid.CreateVersion7();
            string blobRef;
            await using (var stream = archivo.OpenReadStream())
            {
                blobRef = await blob.GuardarAsync(
                    documentoId: facturaId,
                    evidenciaId: evidenciaId,
                    ahora: clock.UtcNow,
                    nombreArchivo: archivo.FileName,
                    contentType: archivo.ContentType ?? "application/octet-stream",
                    contenido: stream,
                    cancellationToken: cancellationToken);
            }

            var response = await mediator.Send(
                new AdjuntarEvidenciaCommand(
                    TipoDocumento: TipoDocumentoEvidencia.FacturaProveedor,
                    DocumentoId: facturaId,
                    Tipo: tipo,
                    ArchivoBlobRef: blobRef,
                    NombreArchivo: archivo.FileName,
                    ContentType: archivo.ContentType ?? "application/octet-stream",
                    TamanioBytes: archivo.Length,
                    Comentario: comentario,
                    EstadoFirmaFisica: estadoFirmaFisica,
                    FechaLimiteFirmaFisica: fechaLimiteFirmaFisica),
                cancellationToken);

            return Results.Created(
                $"/api/v1/cuentas-por-pagar/facturas/{facturaId}/evidencias/{response.Id}",
                response);
        })
        .DisableAntiforgery()
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarFacturasEnviarRevision)
        .WithName("AdjuntarEvidenciaAFactura")
        .WithSummary("Adjunta una evidencia de autorización a una factura (multipart)")
        .WithDescription(
            "Sube el binario a blob storage (stub filesystem en dev) y persiste " +
            "la evidencia con metadata. Campos del multipart: `tipo` " +
            "(CapturaWhatsapp/Audio/Email/FirmaEscaneada/Otro), `comentario` " +
            "(quién autorizó, cuándo, medio), `estadoFirmaFisica` (NoAplica/Pendiente/Recibida), " +
            "`fechaLimiteFirmaFisica` (obligatoria si Pendiente), `archivo` (IFormFile).")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<AdjuntarEvidenciaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
