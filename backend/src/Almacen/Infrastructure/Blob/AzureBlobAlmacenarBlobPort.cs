using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Almacen.Domain.Ports.Blob;

namespace Millet.Almacen.Infrastructure.Blob;

/// <summary>
/// Implementación real (F2-PR4) de <see cref="IAlmacenarBlobPort"/>
/// usando Azure Blob Storage. Reemplaza
/// <see cref="Stubs.LocalFilesystemAlmacenBlobStub"/> en environments
/// con connection string configurada (QA / Prod); el stub local sigue
/// activo en dev cuando la connection string está vacía.
///
/// <para>
/// El container se crea automáticamente la primera vez que el servicio
/// arranca. Cada blob se sube como block blob con metadata mínima
/// (<c>blobId</c>, <c>originalName</c>) para trazabilidad.
/// </para>
///
/// <para>
/// La URL devuelta es la URL absoluta del blob (HTTPS), apta para
/// guardarse como <c>packing_list_blob_ref</c> en
/// <c>MovimientoInventario</c>. El acceso desde el endpoint del FE usa
/// <see cref="ObtenerStreamAsync"/> y devuelve el stream del SDK (no
/// se expone la URL al cliente final).
/// </para>
/// </summary>
public sealed class AzureBlobAlmacenarBlobPort : IAlmacenarBlobPort
{
    private readonly BlobServiceClient _serviceClient;
    private readonly AzureBlobOptions _options;
    private readonly ILogger<AzureBlobAlmacenarBlobPort> _logger;
    private BlobContainerClient? _containerClient;

    public AzureBlobAlmacenarBlobPort(
        BlobServiceClient serviceClient,
        IOptions<AzureBlobOptions> options,
        ILogger<AzureBlobAlmacenarBlobPort> logger)
    {
        _serviceClient = serviceClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> SubirAsync(
        Guid blobId,
        Stream contenido,
        string contentType,
        string nombreArchivoOriginal,
        CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);
        var blobName = $"{blobId:D}{InferirExtension(nombreArchivoOriginal, contentType)}";
        var blob = container.GetBlobClient(blobName);

        var headers = new BlobHttpHeaders { ContentType = contentType };
        var metadata = new Dictionary<string, string>
        {
            ["blobId"] = blobId.ToString("D"),
            ["originalName"] = nombreArchivoOriginal,
        };

        await blob.UploadAsync(
            contenido,
            new BlobUploadOptions { HttpHeaders = headers, Metadata = metadata },
            cancellationToken);

        _logger.LogInformation(
            "Azure Blob Almacén subido. BlobId={BlobId} Url={Url} ContentType={ContentType}",
            blobId, blob.Uri, contentType);

        return blob.Uri.ToString();
    }

    public async Task<Stream> ObtenerStreamAsync(string blobUrl, CancellationToken cancellationToken)
    {
        var blob = ResolveBlobFromUrl(blobUrl);
        var response = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
    }

    public async Task<BlobDescriptor> ObtenerDescriptorAsync(string blobUrl, CancellationToken cancellationToken)
    {
        var blob = ResolveBlobFromUrl(blobUrl);
        var response = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
        var details = response.Value.Details;
        // Azure Blob preserva el ContentType al subir + la metadata
        // custom "OriginalFileName" si la pusimos. Si no hay metadata
        // original, el filename queda null y el endpoint usa el blobId
        // del URL como fallback.
        // Convención: en SubirAsync persistimos la metadata "originalName".
        // Si no está, queda null y el endpoint usa el blobId como fallback.
        details.Metadata.TryGetValue("originalName", out var nombreArchivo);
        var contentType = string.IsNullOrWhiteSpace(details.ContentType)
            ? "application/octet-stream"
            : details.ContentType;
        return new BlobDescriptor(response.Value.Content, contentType, nombreArchivo);
    }

    public async Task EliminarAsync(string blobUrl, CancellationToken cancellationToken)
    {
        var blob = ResolveBlobFromUrl(blobUrl);
        var deleted = await blob.DeleteIfExistsAsync(
            DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
        _logger.LogInformation(
            "Azure Blob Almacén eliminar. Url={Url} EliminadoFisicamente={Eliminado}",
            blobUrl, deleted.Value);
    }

    private async Task<BlobContainerClient> GetContainerAsync(CancellationToken ct)
    {
        if (_containerClient is not null) return _containerClient;

        var container = _serviceClient.GetBlobContainerClient(_options.ContainerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        _containerClient = container;
        return container;
    }

    private BlobClient ResolveBlobFromUrl(string blobUrl)
    {
        var uri = new Uri(blobUrl);
        var container = _serviceClient.GetBlobContainerClient(_options.ContainerName);
        var blobName = uri.AbsolutePath
            .TrimStart('/')
            [(_options.ContainerName.Length + 1)..];
        return container.GetBlobClient(blobName);
    }

    private static string InferirExtension(string nombreArchivoOriginal, string contentType)
    {
        var ext = Path.GetExtension(nombreArchivoOriginal);
        if (!string.IsNullOrEmpty(ext)) return ext;

        return contentType.ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
            "text/plain" => ".txt",
            _ => ".bin",
        };
    }
}

/// <summary>
/// Opciones de configuración del backend Azure Blob para Almacén.
/// Sección: <c>Almacen:BlobStorage</c>.
/// </summary>
public sealed class AzureBlobOptions
{
    public const string SectionName = "Almacen:BlobStorage";

    /// <summary>Connection string de Azure Storage (viene de Key Vault en QA/Prod).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Nombre del container donde se almacenan los blobs de Almacén
    /// (packing lists, evidencias de devoluciones, etc.).
    /// </summary>
    public string ContainerName { get; set; } = "almacen-blobs";
}
