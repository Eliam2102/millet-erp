using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Almacen.Domain.Ports.Blob;

namespace Millet.Almacen.Infrastructure.Stubs;

/// <summary>
/// Implementación stub de <see cref="IAlmacenarBlobPort"/> que escribe
/// los blobs al filesystem local. Sirve para desarrollo, tests y como
/// fallback cuando no hay connection string de Azure Blob configurada.
///
/// <para>
/// La URL devuelta tiene formato <c>file://{rutaAbsoluta}</c>. El path
/// root es configurable vía <see cref="LocalFilesystemAlmacenBlobStubOptions"/>
/// (default <see cref="Path.GetTempPath"/> + <c>millet-almacen-blobs</c>).
/// El nombre final del archivo es <c>{blobId}{extension}</c> donde la
/// extensión se infiere del nombre original o del content type.
/// </para>
///
/// <para>
/// F2-PR4: <see cref="Blob.AzureBlobAlmacenarBlobPort"/> reemplaza este
/// stub cuando <c>Almacen:BlobStorage:ConnectionString</c> está
/// configurado (Key Vault en QA/Prod). Sin connection string, se sigue
/// usando este stub local — útil para dev.
/// </para>
/// </summary>
public sealed class LocalFilesystemAlmacenBlobStub : IAlmacenarBlobPort
{
    private readonly LocalFilesystemAlmacenBlobStubOptions _options;
    private readonly ILogger<LocalFilesystemAlmacenBlobStub> _logger;

    public LocalFilesystemAlmacenBlobStub(
        IOptions<LocalFilesystemAlmacenBlobStubOptions> options,
        ILogger<LocalFilesystemAlmacenBlobStub> logger)
    {
        _options = options.Value;
        _logger = logger;
        EnsureRootDirectory();
    }

    public async Task<string> SubirAsync(
        Guid blobId,
        Stream contenido,
        string contentType,
        string nombreArchivoOriginal,
        CancellationToken cancellationToken)
    {
        var extension = InferirExtension(nombreArchivoOriginal, contentType);
        var fileName = $"{blobId:D}{extension}";
        var fullPath = Path.Combine(_options.RootPath, fileName);

        await using var fs = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        await contenido.CopyToAsync(fs, cancellationToken);

        var url = $"file://{fullPath.Replace('\\', '/')}";
        _logger.LogInformation(
            "LocalFilesystemAlmacenBlobStub: blob {BlobId} subido a {Url} ({Size} bytes)",
            blobId, url, fs.Length);
        return url;
    }

    public Task<Stream> ObtenerStreamAsync(string blobUrl, CancellationToken cancellationToken)
    {
        var path = UrlToPath(blobUrl);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Blob no encontrado en el filesystem stub: {blobUrl}", path);
        }
        Stream stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task<BlobDescriptor> ObtenerDescriptorAsync(string blobUrl, CancellationToken cancellationToken)
    {
        var path = UrlToPath(blobUrl);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Blob no encontrado en el filesystem stub: {blobUrl}", path);
        }
        Stream stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        // El stub no preserva el nombre original (solo guarda {blobId}{ext}).
        // El ContentType se infiere de la extensión del archivo en disco —
        // suficiente para que el browser previsualice PDF/imagen al abrir.
        var fileName = Path.GetFileName(path);
        var contentType = ResolverContentType(Path.GetExtension(path));
        return Task.FromResult(new BlobDescriptor(stream, contentType, fileName));
    }

    public Task EliminarAsync(string blobUrl, CancellationToken cancellationToken)
    {
        var path = UrlToPath(blobUrl);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogInformation("LocalFilesystemAlmacenBlobStub: blob eliminado {Url}", blobUrl);
        }
        else
        {
            _logger.LogDebug("LocalFilesystemAlmacenBlobStub: eliminar {Url} no-op (no existe)", blobUrl);
        }
        return Task.CompletedTask;
    }

    private void EnsureRootDirectory()
    {
        if (!Directory.Exists(_options.RootPath))
        {
            Directory.CreateDirectory(_options.RootPath);
            _logger.LogInformation(
                "LocalFilesystemAlmacenBlobStub: directorio de blobs creado en {RootPath}",
                _options.RootPath);
        }
    }

    private static string UrlToPath(string blobUrl)
    {
        const string prefix = "file://";
        if (!blobUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"BlobUrl no reconocida por LocalFilesystemAlmacenBlobStub: '{blobUrl}'.",
                nameof(blobUrl));
        }
        return blobUrl[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
    }

    private static string ResolverContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".txt" => "text/plain",
        _ => "application/octet-stream",
    };

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

/// <summary>Opciones de configuración del stub local de blobs de Almacén.</summary>
public sealed class LocalFilesystemAlmacenBlobStubOptions
{
    public const string SectionName = "Almacen:BlobStub";

    /// <summary>
    /// Directorio root donde se almacenan los blobs. Default:
    /// <c>{TempPath}/millet-almacen-blobs</c>. Se crea si no existe al
    /// arrancar el servicio.
    /// </summary>
    public string RootPath { get; set; } = Path.Combine(Path.GetTempPath(), "millet-almacen-blobs");
}
