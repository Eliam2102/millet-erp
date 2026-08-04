using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Domain.Ports.Blob;

namespace Millet.Integraciones.Aw.Infrastructure.Blob;

/// <summary>
/// Implementación real de <see cref="IAlmacenarBlobPort"/> usando Azure
/// Blob Storage. Réplica del adapter de Compras/Almacén — mismo patrón:
/// block blob con metadata mínima, container auto-creado, URL absoluta
/// HTTPS de retorno. Se selecciona cuando
/// <c>IntegracionesAw:BlobStorage:ConnectionString</c> está configurado;
/// sin él, se usa <c>LocalFilesystemBlobStub</c> (dev local).
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
            "Azure Blob (Aw) subido. BlobId={BlobId} Url={Url} ContentType={ContentType}",
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
            "Azure Blob (Aw) eliminar. Url={Url} EliminadoFisicamente={Eliminado}",
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
            "text/plain" => ".txt",
            _ => ".bin",
        };
    }
}

/// <summary>Opciones de configuración del backend Azure Blob del módulo Aw.</summary>
public sealed class AzureBlobOptions
{
    public const string SectionName = "IntegracionesAw:BlobStorage";

    /// <summary>Connection string de Azure Storage (viene de Key Vault en QA/Prod).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Nombre del container donde se almacenan los PDF de A+W.</summary>
    public string ContainerName { get; set; } = "integraciones-aw-blobs";
}
