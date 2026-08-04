using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.CuentasPorPagar.Domain.Cfdi;

namespace Millet.CuentasPorPagar.Infrastructure.Blob;

/// <summary>
/// Adapter Azure Blob real de <see cref="ICfdiBlobStorage"/>. Cierra el
/// PLATFORM-TODO(&lt;CfdiBlobStorage&gt;) — antes los XML/PDF vivían en
/// el filesystem efímero del App Service y se perdían en cada deploy.
///
/// <para>
/// A diferencia de los adapters de Compras/Almacén (que persisten la URL
/// absoluta), aquí la referencia guardada en
/// <c>CfdiRecibido.XmlBlobRef</c> es el path relativo
/// <c>cxp/{año}/{mes}/cfdi/{uuid}.{ext}</c> — la MISMA convención que el
/// stub filesystem (ADR-0024), de modo que las refs son portables entre
/// backends y el blob name es directamente la ref.
/// </para>
/// </summary>
public sealed class AzureBlobCfdiBlobStorage : ICfdiBlobStorage
{
    private readonly BlobServiceClient _serviceClient;
    private readonly AzureCfdiBlobOptions _options;
    private readonly ILogger<AzureBlobCfdiBlobStorage> _logger;
    private BlobContainerClient? _containerClient;

    public AzureBlobCfdiBlobStorage(
        BlobServiceClient serviceClient,
        IOptions<AzureCfdiBlobOptions> options,
        ILogger<AzureBlobCfdiBlobStorage> logger)
    {
        _serviceClient = serviceClient;
        _options = options.Value;
        _logger = logger;
    }

    public Task<string> GuardarXmlAsync(
        string uuid,
        DateTimeOffset fechaCfdi,
        Stream contenido,
        CancellationToken cancellationToken)
        => GuardarAsync(uuid, fechaCfdi, contenido, "xml", "application/xml", cancellationToken);

    public async Task<string?> GuardarPdfAsync(
        string uuid,
        DateTimeOffset fechaCfdi,
        Stream? contenido,
        CancellationToken cancellationToken)
    {
        if (contenido is null) return null;
        return await GuardarAsync(uuid, fechaCfdi, contenido, "pdf", "application/pdf", cancellationToken);
    }

    public Task<Stream?> LeerXmlAsync(string blobRef, CancellationToken cancellationToken)
        => LeerAsync(blobRef, cancellationToken);

    public Task<Stream?> LeerPdfAsync(string blobRef, CancellationToken cancellationToken)
        => LeerAsync(blobRef, cancellationToken);

    private async Task<string> GuardarAsync(
        string uuid,
        DateTimeOffset fechaCfdi,
        Stream contenido,
        string ext,
        string contentType,
        CancellationToken cancellationToken)
    {
        var fechaUtc = fechaCfdi.UtcDateTime;
        var blobRef = $"cxp/{fechaUtc:yyyy}/{fechaUtc:MM}/cfdi/{uuid.ToUpperInvariant()}.{ext}";

        var container = await GetContainerAsync(cancellationToken);
        var blob = container.GetBlobClient(blobRef);

        // Idempotente por UUID (contrato del puerto): overwrite=true — re-subir
        // el mismo UUID con el mismo contenido devuelve la misma ref sin error.
        await blob.UploadAsync(
            contenido,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Metadata = new Dictionary<string, string> { ["uuidCfdi"] = uuid.ToUpperInvariant() },
            },
            cancellationToken);

        _logger.LogInformation(
            "[AzureBlobCfdiBlobStorage] Guardado {Ref} ({ContentType})", blobRef, contentType);
        return blobRef;
    }

    private async Task<Stream?> LeerAsync(string blobRef, CancellationToken cancellationToken)
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
            _logger.LogWarning("[AzureBlobCfdiBlobStorage] Blob no encontrado {Ref}", blobRef);
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
/// Opciones del backend Azure Blob de CFDIs. Comparte la sección con el
/// stub filesystem (<c>RootPath</c>): la presencia de
/// <see cref="ConnectionString"/> es el toggle que elige adapter real.
/// </summary>
public sealed class AzureCfdiBlobOptions
{
    public const string SectionName = "CuentasPorPagar:Cfdi:BlobStorage";

    /// <summary>Connection string de Azure Storage (viene de Key Vault en QA/Prod).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Container de los XML/PDF de CFDIs recibidos.</summary>
    public string ContainerName { get; set; } = "cuentas-por-pagar-cfdis";
}
