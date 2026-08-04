using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Compras.Domain.Ports.Blob;

namespace Millet.Compras.Infrastructure.Oc.Blob;

/// <summary>
/// Implementación real (F10-PR3) de <see cref="IAlmacenarBlobPort"/>
/// usando Azure Blob Storage. Reemplaza
/// <c>LocalFilesystemBlobStub</c> en environments con connection string
/// configurada (Production y staging); el stub local sigue activo en
/// dev cuando la connection string está vacía.
///
/// <para>
/// El container se crea automáticamente la primera vez que el servicio
/// arranca. Cada blob se sube como block blob con metadata mínima
/// (<c>blobId</c>, <c>contentType</c>, <c>originalName</c>) para
/// trazabilidad.
/// </para>
///
/// <para>
/// La URL devuelta es la URL absoluta del blob (HTTPS), apta para
/// guardarse en <c>OrdenCompraPdf.BlobUrl</c> o <c>AdjuntoOC.BlobUrl</c>.
/// El acceso desde el endpoint <c>GET /pdf</c> usa
/// <see cref="ObtenerStreamAsync"/> y devuelve el stream del SDK
/// (no se expone la URL al cliente final).
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
            "Azure Blob subido. BlobId={BlobId} Url={Url} ContentType={ContentType}",
            blobId, blob.Uri, contentType);

        return blob.Uri.ToString();
    }

    public async Task<Stream> ObtenerStreamAsync(string blobUrl, CancellationToken cancellationToken)
    {
        var blob = ResolveBlobFromUrl(blobUrl);
        var response = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
    }

    public async Task EliminarAsync(string blobUrl, CancellationToken cancellationToken)
    {
        var blob = ResolveBlobFromUrl(blobUrl);
        var deleted = await blob.DeleteIfExistsAsync(
            DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
        _logger.LogInformation(
            "Azure Blob eliminar. Url={Url} EliminadoFisicamente={Eliminado}",
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
        // BlobClient(string url) acepta la URL absoluta directa. El SDK
        // resuelve container + blob name. La autenticación viene del
        // _serviceClient compartido (mismas credenciales).
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
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
            "text/plain" => ".txt",
            _ => ".bin",
        };
    }
}

/// <summary>Opciones de configuración del backend Azure Blob.</summary>
public sealed class AzureBlobOptions
{
    public const string SectionName = "Compras:Oc:BlobStorage";

    /// <summary>Connection string de Azure Storage (viene de Key Vault en QA/Prod).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Nombre del container donde se almacenan los blobs de OC.</summary>
    public string ContainerName { get; set; } = "compras-oc-blobs";
}
