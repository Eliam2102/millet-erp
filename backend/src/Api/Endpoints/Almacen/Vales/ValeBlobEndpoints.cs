using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports.Blob;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Almacen.Vales;

/// <summary>
/// Endpoint de upload del archivo del vale urgente (F5-PR1, Variante B
/// de salidas). Análogo al de packing list (F2-PR4) pero gateado por
/// <c>almacen.salidas.por-vale</c>.
///
/// <para>Flujo de dos pasos para Salida por Vale (A14 del levantamiento):</para>
/// <list type="number">
///   <item>FE sube el vale escaneado/firmado a
///     <c>POST /api/v1/almacen/salidas/vale/blob</c> → recibe
///     <c>{ blobRef }</c>.</item>
///   <item>FE llama <c>POST /api/v1/almacen/salidas/vale</c> con
///     <c>valeBlobRef = blobRef</c>.</item>
/// </list>
///
/// <para>Reusa el mismo puerto <c>IAlmacenarBlobPort</c> de Almacén
/// (PR #256) — el container del módulo aloja los dos tipos de
/// adjunto sin distinción.</para>
/// </summary>
public static class ValeBlobEndpoints
{
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

    public static IEndpointRouteBuilder MapValeBlobEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/almacen/salidas/vale/blob", async (
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
                    $"Tipo MIME '{contentType}' no permitido para vale. " +
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
                new SubirValeResponse(blobRef, archivo.FileName, archivo.Length, contentType));
        })
        .DisableAntiforgery()
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasPorVale)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithTags("Almacen")
        .WithName("SubirValeBlob")
        .WithSummary("Sube el archivo del vale urgente (F5-PR1, A14)")
        .WithDescription(
            "Sube el vale escaneado/firmado al blob storage de Almacén y devuelve la " +
            "blob ref que el FE debe pasar en `valeBlobRef` al endpoint de registro de " +
            "la salida por vale. Permiso `almacen.salidas.por-vale` + `Idempotency-Key` " +
            "obligatorio. Máximo 20 MB.")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<SubirValeResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // GET /api/v1/almacen/salidas/{id}/vale → descarga/preview del vale
        // escaneado de una salida. El FE lo abre en nueva pestaña; browser
        // previsualiza PDF/imagen inline. Por defecto sin
        // Content-Disposition: attachment para permitir el inline preview;
        // si el caller pasa `?download=true` se fuerza el download.
        app.MapGet("/api/v1/almacen/salidas/{id:guid}/vale", async (
            Guid id,
            bool? download,
            AlmacenDbContext db,
            IAlmacenarBlobPort blob,
            CancellationToken cancellationToken) =>
        {
            var salida = await db.Movimientos
                .AsNoTracking()
                .Where(m => m.Id == id
                    && (m.Tipo == TipoMovimiento.SalidaConsumo
                        || m.Tipo == TipoMovimiento.SalidaPorVale))
                .Select(m => new { m.Id, m.ValeBlobRef, m.Folio })
                .FirstOrDefaultAsync(cancellationToken);

            if (salida is null)
            {
                return Results.NotFound();
            }
            if (string.IsNullOrWhiteSpace(salida.ValeBlobRef))
            {
                throw new BusinessRuleException(
                    "SALIDA_SIN_VALE",
                    "Esta salida no tiene vale escaneado adjunto.");
            }

            var descriptor = await blob.ObtenerDescriptorAsync(salida.ValeBlobRef, cancellationToken);

            // Filename amistoso: vale-{folio}{ext} si conocemos el original.
            var ext = !string.IsNullOrWhiteSpace(descriptor.NombreArchivo)
                ? Path.GetExtension(descriptor.NombreArchivo)
                : InferirExtensionDeContentType(descriptor.ContentType);
            var fileName = $"vale-{salida.Folio ?? id.ToString()}{ext}";

            return Results.Stream(
                descriptor.Contenido,
                descriptor.ContentType,
                fileDownloadName: download is true ? fileName : null,
                enableRangeProcessing: true);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasLeerTodas)
        .WithTags("Almacen")
        .WithName("DescargarValeBlob")
        .WithSummary("Descarga/preview del vale escaneado de una salida")
        .WithDescription(
            "Devuelve el contenido binario del vale (PDF/imagen/Office) con el " +
            "ContentType correcto para que el browser lo previsualice inline. " +
            "Pasar `?download=true` para forzar Content-Disposition: attachment.")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static string InferirExtensionDeContentType(string contentType) =>
        contentType.ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
            "text/plain" => ".txt",
            _ => string.Empty,
        };
}

/// <summary>Respuesta del upload del vale.</summary>
public sealed record SubirValeResponse(
    string BlobRef,
    string NombreArchivo,
    long TamanoBytes,
    string ContentType);
