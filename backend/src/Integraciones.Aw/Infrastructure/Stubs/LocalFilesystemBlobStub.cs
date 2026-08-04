using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Domain.Ports.Blob;

namespace Millet.Integraciones.Aw.Infrastructure.Stubs;

/// <summary>
/// Implementación stub de <see cref="IAlmacenarBlobPort"/> que escribe los
/// blobs al filesystem local. Sirve para desarrollo, tests y como fallback
/// hasta que se wirée Azure Blob Storage real. Réplica del stub de
/// Compras/Almacén. La URL devuelta tiene formato <c>file://{rutaAbsoluta}</c>.
/// </summary>
public sealed class LocalFilesystemBlobStub : IAlmacenarBlobPort
{
    private readonly LocalFilesystemBlobStubOptions _options;
    private readonly ILogger<LocalFilesystemBlobStub> _logger;

    public LocalFilesystemBlobStub(
        IOptions<LocalFilesystemBlobStubOptions> options,
        ILogger<LocalFilesystemBlobStub> logger)
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
            fullPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        await contenido.CopyToAsync(fs, cancellationToken);

        var url = $"file://{fullPath.Replace('\\', '/')}";
        _logger.LogInformation(
            "LocalFilesystemBlobStub (Aw): blob {BlobId} subido a {Url} ({Size} bytes)",
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

    public Task EliminarAsync(string blobUrl, CancellationToken cancellationToken)
    {
        var path = UrlToPath(blobUrl);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogInformation("LocalFilesystemBlobStub (Aw): blob eliminado {Url}", blobUrl);
        }
        return Task.CompletedTask;
    }

    private void EnsureRootDirectory()
    {
        if (!Directory.Exists(_options.RootPath))
        {
            Directory.CreateDirectory(_options.RootPath);
            _logger.LogInformation(
                "LocalFilesystemBlobStub (Aw): directorio de blobs creado en {RootPath}",
                _options.RootPath);
        }
    }

    private static string UrlToPath(string blobUrl)
    {
        const string prefix = "file://";
        if (!blobUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"BlobUrl no reconocida por LocalFilesystemBlobStub: '{blobUrl}'.",
                nameof(blobUrl));
        }
        return blobUrl[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
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

/// <summary>Opciones de configuración del stub local de blobs del módulo Aw.</summary>
public sealed class LocalFilesystemBlobStubOptions
{
    public const string SectionName = "IntegracionesAw:BlobStub";

    /// <summary>
    /// Directorio root donde se almacenan los blobs. Default:
    /// <c>{TempPath}/millet-aw-blobs</c>. Se crea si no existe al arrancar.
    /// </summary>
    public string RootPath { get; set; } = Path.Combine(Path.GetTempPath(), "millet-aw-blobs");
}
