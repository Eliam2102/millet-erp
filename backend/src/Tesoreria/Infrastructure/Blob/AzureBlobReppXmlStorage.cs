using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Tesoreria.Domain.Ports;

namespace Millet.Tesoreria.Infrastructure.Blob;

/// <summary>
/// Opciones del storage de XML de REPP recibidos (TES-PR8). Comparte la
/// sección con el stub filesystem (<see cref="RootPath"/>): la presencia
/// de <see cref="ConnectionString"/> es el toggle que elige adapter real
/// — mismo patrón que <c>AzureCfdiBlobOptions</c> de CxP. App setting
/// <c>Tesoreria__Repp__BlobStorage__ConnectionString</c> en
/// <c>appservice.bicep</c>.
/// </summary>
public sealed class ReppBlobStorageOptions
{
    public const string SectionName = "Tesoreria:Repp:BlobStorage";

    /// <summary>Connection string de Azure Storage (Key Vault en dev/QA/Prod).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Container de los XML de complementos de pago recibidos.</summary>
    public string ContainerName { get; set; } = "tesoreria-repp";

    /// <summary>Path raíz del stub filesystem en dev local.</summary>
    public string RootPath { get; set; } = "./blob-tesoreria";
}

/// <summary>
/// Adapter Azure Blob real de <see cref="IReppXmlBlobStorage"/> (TES-PR8,
/// ADR-0024). La referencia persistida es el path relativo
/// <c>tesoreria/{año}/{mes}/repp/{uuid}.xml</c> — portable entre este
/// backend y el stub filesystem.
/// </summary>
public sealed class AzureBlobReppXmlStorage : IReppXmlBlobStorage
{
    private readonly BlobServiceClient _serviceClient;
    private readonly ReppBlobStorageOptions _options;
    private readonly ILogger<AzureBlobReppXmlStorage> _logger;
    private BlobContainerClient? _containerClient;

    public AzureBlobReppXmlStorage(
        BlobServiceClient serviceClient,
        IOptions<ReppBlobStorageOptions> options,
        ILogger<AzureBlobReppXmlStorage> logger)
    {
        _serviceClient = serviceClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> GuardarXmlAsync(
        string uuid,
        DateOnly fechaComplemento,
        Stream contenido,
        CancellationToken cancellationToken)
    {
        var blobRef = ReppBlobPath.Construir(uuid, fechaComplemento);

        var container = await GetContainerAsync(cancellationToken);
        var blob = container.GetBlobClient(blobRef);

        await blob.UploadAsync(
            contenido,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/xml" },
                Metadata = new Dictionary<string, string> { ["uuidComplemento"] = uuid.ToUpperInvariant() },
            },
            cancellationToken);

        _logger.LogInformation("[AzureBlobReppXmlStorage] Guardado {Ref}", blobRef);
        return blobRef;
    }

    public async Task<Stream?> LeerXmlAsync(string blobRef, CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);
        var blob = container.GetBlobClient(blobRef);

        try
        {
            var response = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return response.Value.Content;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("[AzureBlobReppXmlStorage] Blob no encontrado {Ref}", blobRef);
            return null;
        }
    }

    private async Task<BlobContainerClient> GetContainerAsync(CancellationToken ct)
    {
        if (_containerClient is not null) return _containerClient;

        var container = _serviceClient.GetBlobContainerClient(_options.ContainerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        _containerClient = container;
        return container;
    }
}

/// <summary>
/// Stub filesystem de <see cref="IReppXmlBlobStorage"/> para dev local
/// sin Azure Storage (mismo criterio que
/// <c>LocalFilesystemCfdiBlobStorage</c> de CxP): efímero en el container
/// Linux — en Azure siempre va el adapter real vía connection string.
/// </summary>
public sealed class LocalFilesystemReppXmlStorage : IReppXmlBlobStorage
{
    private readonly ReppBlobStorageOptions _options;
    private readonly ILogger<LocalFilesystemReppXmlStorage> _logger;

    public LocalFilesystemReppXmlStorage(
        IOptions<ReppBlobStorageOptions> options,
        ILogger<LocalFilesystemReppXmlStorage> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> GuardarXmlAsync(
        string uuid,
        DateOnly fechaComplemento,
        Stream contenido,
        CancellationToken cancellationToken)
    {
        var relPath = ReppBlobPath.Construir(uuid, fechaComplemento);
        var fullPath = Path.Combine(_options.RootPath, relPath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (var fs = File.Create(fullPath))
        {
            await contenido.CopyToAsync(fs, cancellationToken);
        }

        _logger.LogDebug("[LocalFilesystemReppXmlStorage] Guardado {Path}", relPath);
        return relPath;
    }

    public Task<Stream?> LeerXmlAsync(string blobRef, CancellationToken cancellationToken)
    {
        var fullPath = Path.Combine(_options.RootPath, blobRef);
        if (!File.Exists(fullPath))
        {
            _logger.LogWarning("[LocalFilesystemReppXmlStorage] Blob no encontrado {Ref}", blobRef);
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(File.OpenRead(fullPath));
    }
}

internal static class ReppBlobPath
{
    /// <summary>Convención ADR-0024: <c>tesoreria/{año}/{mes}/repp/{uuid}.xml</c>.</summary>
    public static string Construir(string uuid, DateOnly fechaComplemento) =>
        $"tesoreria/{fechaComplemento:yyyy}/{fechaComplemento:MM}/repp/{uuid.ToUpperInvariant()}.xml";
}
