using Microsoft.AspNetCore.Http;
using Millet.Almacen.Domain.Ports.Blob;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Almacen.DevolucionesProveedor;

/// <summary>
/// Endpoint de upload de evidencias de devolución a proveedor (F6-PR1,
/// sub-flujo 8.B). Análogo a packing list (PR #256) y vale (PR #258);
/// el FE sube el archivo, recibe la <c>blobRef</c>, y la pasa al
/// comando <c>AdjuntarEvidenciaDevolucionAProveedorCommand</c> junto
/// con metadata (tipo, nombre, comentario).
///
/// <para>El backend de devolución a proveedor exige al menos una
/// evidencia antes de pasar a <c>EnAutorizacion</c>; este endpoint
/// existe para que el FE pueda capturar fotos / emails / cualquier
/// soporte visual del defecto.</para>
/// </summary>
public static class EvidenciaBlobEndpoints
{
    private const long MaxBytes = 20 * 1024 * 1024;

    private static readonly string[] MimeWhitelist =
    [
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/heic",
        "image/heif",
        "video/mp4",
        "video/quicktime",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/msword",
        "message/rfc822", // emails .eml
        "text/plain",
    ];

    public static IEndpointRouteBuilder MapEvidenciaBlobEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/almacen/devoluciones-proveedor/evidencia/blob", async (
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
                    $"Tipo MIME '{contentType}' no permitido para evidencia. " +
                    $"Aceptados: PDF, imágenes (incluye HEIC), video corto (MP4/MOV), Office, email (.eml) y texto plano.");
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
                new SubirEvidenciaResponse(blobRef, archivo.FileName, archivo.Length, contentType));
        })
        .DisableAntiforgery()
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenDevolucionesProveedorIniciar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithTags("Almacen")
        .WithName("SubirEvidenciaDevolucionProveedor")
        .WithSummary("Sube una evidencia (foto, email, video) de devolución a proveedor")
        .WithDescription(
            "Sube el archivo al blob storage de Almacén y devuelve la blob ref que el " +
            "FE pasa después al endpoint POST /devoluciones-proveedor/{id}/evidencias junto " +
            "con el tipo de evidencia + comentario. Permiso " +
            "`almacen.devoluciones-proveedor.iniciar` + `Idempotency-Key` obligatorio. Máximo " +
            "20 MB. Acepta imágenes (incluye HEIC), video corto, PDF, Office y .eml.")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<SubirEvidenciaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}

/// <summary>Respuesta del upload de una evidencia.</summary>
public sealed record SubirEvidenciaResponse(
    string BlobRef,
    string NombreArchivo,
    long TamanoBytes,
    string ContentType);
