using Microsoft.AspNetCore.Http;
using Millet.Almacen.Domain.Ports.Blob;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Almacen.Recepciones;

/// <summary>
/// Endpoint de upload del packing list de una recepción Variante B
/// (F2-PR4). El flujo es de dos pasos:
/// <list type="number">
///   <item>FE sube el archivo a <c>POST /api/v1/almacen/recepciones/packing-list/blob</c>
///     → recibe <c>{ blobRef }</c>.</item>
///   <item>FE llama <c>POST /api/v1/almacen/recepciones/packing-list</c>
///     con <c>packingListBlobRef = blobRef</c>.</item>
/// </list>
/// Separar el upload de la creación evita multipart en el endpoint de
/// negocio (que ya es transaccional + emite eventos) y permite reusar
/// el blob si la creación falla por validación y el usuario reintenta.
///
/// <para>Permiso: <c>almacen.entradas.registrar</c> (el mismo que
/// crea la recepción). Idempotency-Key obligatorio (evita doble-upload
/// en retry).</para>
/// </summary>
public static class PackingListBlobEndpoints
{
    /// <summary>Tope de tamaño del archivo (20 MB, alineado a §4.11 de Compras OC).</summary>
    private const long MaxBytes = 20 * 1024 * 1024;

    private static readonly string[] MimeWhitelist =
    [
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/webp",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/msword",
        "text/plain",
    ];

    public static IEndpointRouteBuilder MapPackingListBlobEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/almacen/recepciones/packing-list/blob", async (
            IFormFile archivo,
            IAlmacenarBlobPort blob,
            CancellationToken cancellationToken) =>
        {
            if (archivo is null || archivo.Length == 0)
            {
                throw new BusinessRuleException(
                    "ARCHIVO_REQUERIDO",
                    "Debe enviarse un archivo en el campo 'archivo' del multipart.");
            }

            if (archivo.Length > MaxBytes)
            {
                throw new BusinessRuleException(
                    "ARCHIVO_DEMASIADO_GRANDE",
                    $"El archivo excede el tamaño máximo permitido ({MaxBytes / (1024 * 1024)} MB).");
            }

            var contentType = archivo.ContentType ?? "application/octet-stream";
            if (!MimeWhitelist.Contains(contentType.ToLowerInvariant()))
            {
                throw new BusinessRuleException(
                    "MIME_NO_PERMITIDO",
                    $"Tipo MIME '{contentType}' no permitido para packing list. " +
                    $"Aceptados: PDF, imágenes (JPEG/PNG/WebP), Office (DOC/DOCX/XLS/XLSX) y texto plano.");
            }

            var blobId = Guid.CreateVersion7();
            string blobRef;
            await using (var stream = archivo.OpenReadStream())
            {
                blobRef = await blob.SubirAsync(
                    blobId,
                    stream,
                    contentType,
                    archivo.FileName,
                    cancellationToken);
            }

            return Results.Created(
                blobRef,
                new SubirPackingListResponse(blobRef, archivo.FileName, archivo.Length, contentType));
        })
        .DisableAntiforgery() // Multipart en API REST con JWT bearer.
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenEntradasRegistrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithTags("Almacen")
        .WithName("SubirPackingListBlob")
        .WithSummary("Sube el packing list de una recepción Variante B (F2-PR4)")
        .WithDescription(
            "Sube un archivo al blob storage de Almacén y devuelve la blob ref " +
            "que el FE debe pasar en `packingListBlobRef` al endpoint de creación " +
            "de la recepción Variante B. Permiso `almacen.entradas.registrar` + " +
            "`Idempotency-Key` obligatorio. Máximo 20 MB.")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<SubirPackingListResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}

/// <summary>Respuesta del upload de packing list.</summary>
public sealed record SubirPackingListResponse(
    string BlobRef,
    string NombreArchivo,
    long TamanoBytes,
    string ContentType);
