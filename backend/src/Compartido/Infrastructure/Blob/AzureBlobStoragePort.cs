using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using Millet.SharedKernel.Application.Blob;

namespace Millet.Compartido.Infrastructure.Blob;

/// <summary>Implementación de <see cref="IBlobStoragePort"/> sobre Azure Blob Storage (contenedor privado).</summary>
public sealed class AzureBlobStoragePort : IBlobStoragePort
{
    private readonly BlobServiceClient _serviceClient;
    private readonly string _containerName;
    private BlobContainerClient? _container;

    public AzureBlobStoragePort(BlobServiceClient serviceClient, IOptions<AdjuntosBlobStorageOptions> options)
    {
        _serviceClient = serviceClient;
        _containerName = options.Value.ContainerName;
    }

    public async Task SubirAsync(string clave, Stream contenido, string contentType, CancellationToken cancellationToken)
    {
        var container = await ObtenerContenedorAsync(cancellationToken);
        await container.GetBlobClient(clave).UploadAsync(
            contenido,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);
    }

    public async Task<Stream> ObtenerStreamAsync(string clave, CancellationToken cancellationToken)
    {
        var container = await ObtenerContenedorAsync(cancellationToken);
        try
        {
            var r = await container.GetBlobClient(clave).DownloadStreamingAsync(cancellationToken: cancellationToken);
            return r.Value.Content;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new FileNotFoundException("Blob no encontrado.", clave, ex);
        }
    }

    public async Task EliminarAsync(string clave, CancellationToken cancellationToken)
    {
        var container = await ObtenerContenedorAsync(cancellationToken);
        await container.GetBlobClient(clave).DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
    }

    private async Task<BlobContainerClient> ObtenerContenedorAsync(CancellationToken ct)
    {
        if (_container is not null) return _container;
        var c = _serviceClient.GetBlobContainerClient(_containerName);
        await c.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        return _container = c;
    }
}
