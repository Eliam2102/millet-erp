using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.CuentasPorPagar.Domain.Cfdi;

namespace Millet.CuentasPorPagar.Infrastructure.Blob;

public sealed class LocalFilesystemCfdiBlobStorageOptions
{
    public const string SectionName = "CuentasPorPagar:Cfdi:BlobStorage";

    /// <summary>
    /// Path raíz donde se escriben los XML/PDF en dev local. Default
    /// <c>./blob-cxp</c> relativo al cwd del proceso. En QA/Prod no se
    /// usa este stub (se reemplaza por Azure Blob).
    /// </summary>
    public string RootPath { get; init; } = "./blob-cxp";
}

/// <summary>
/// Stub de <see cref="ICfdiBlobStorage"/> que guarda los XML/PDF en el
/// filesystem local (F1-PR1). PLATFORM-TODO(&lt;CfdiBlobStorage&gt;):
/// reemplazar por adapter Azure Blob real en F10 (mismo patrón que
/// Compras OC blob — config-driven con fallback al stub).
/// </summary>
public sealed class LocalFilesystemCfdiBlobStorage : ICfdiBlobStorage
{
    private readonly LocalFilesystemCfdiBlobStorageOptions _options;
    private readonly ILogger<LocalFilesystemCfdiBlobStorage> _logger;

    public LocalFilesystemCfdiBlobStorage(
        IOptions<LocalFilesystemCfdiBlobStorageOptions> options,
        ILogger<LocalFilesystemCfdiBlobStorage> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<string> GuardarXmlAsync(
        string uuid,
        DateTimeOffset fechaCfdi,
        Stream contenido,
        CancellationToken cancellationToken)
        => GuardarAsync(uuid, fechaCfdi, contenido, "xml", cancellationToken)!;

    public async Task<string?> GuardarPdfAsync(
        string uuid,
        DateTimeOffset fechaCfdi,
        Stream? contenido,
        CancellationToken cancellationToken)
    {
        if (contenido is null) return null;
        return await GuardarAsync(uuid, fechaCfdi, contenido, "pdf", cancellationToken);
    }

    public Task<Stream?> LeerXmlAsync(string blobRef, CancellationToken cancellationToken)
        => LeerAsync(blobRef);

    public Task<Stream?> LeerPdfAsync(string blobRef, CancellationToken cancellationToken)
        => LeerAsync(blobRef);

    private Task<Stream?> LeerAsync(string blobRef)
    {
        var fullPath = Path.Combine(_options.RootPath, blobRef);
        if (!File.Exists(fullPath))
        {
            _logger.LogWarning(
                "[LocalFilesystemCfdiBlobStorage] Blob no encontrado {Ref}", blobRef);
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(File.OpenRead(fullPath));
    }

    private async Task<string> GuardarAsync(
        string uuid,
        DateTimeOffset fechaCfdi,
        Stream contenido,
        string ext,
        CancellationToken cancellationToken)
    {
        var fechaUtc = fechaCfdi.UtcDateTime;
        var relPath = $"cxp/{fechaUtc:yyyy}/{fechaUtc:MM}/cfdi/{uuid.ToUpperInvariant()}.{ext}";
        var fullPath = Path.Combine(_options.RootPath, relPath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (var fs = File.Create(fullPath))
        {
            await contenido.CopyToAsync(fs, cancellationToken);
        }

        _logger.LogDebug("[LocalFilesystemCfdiBlobStorage] Guardado {Path}", relPath);
        return relPath;
    }
}
