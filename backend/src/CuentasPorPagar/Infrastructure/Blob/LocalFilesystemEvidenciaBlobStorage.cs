using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.CuentasPorPagar.Domain.Evidencias;

namespace Millet.CuentasPorPagar.Infrastructure.Blob;

/// <summary>
/// Stub local filesystem para evidencias (F4-PR2). Reutiliza el mismo
/// <c>RootPath</c> que <c>LocalFilesystemCfdiBlobStorageOptions</c>.
/// PLATFORM-TODO(&lt;EvidenciaBlobStorage&gt;): adapter Azure Blob en
/// F10 con container privado + SAS tokens cortos (§6.2 del
/// 04-cuidados-infra).
/// </summary>
public sealed class LocalFilesystemEvidenciaBlobStorage : IEvidenciaBlobStorage
{
    private readonly LocalFilesystemCfdiBlobStorageOptions _options;
    private readonly ILogger<LocalFilesystemEvidenciaBlobStorage> _logger;

    public LocalFilesystemEvidenciaBlobStorage(
        IOptions<LocalFilesystemCfdiBlobStorageOptions> options,
        ILogger<LocalFilesystemEvidenciaBlobStorage> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> GuardarAsync(
        Guid documentoId,
        Guid evidenciaId,
        DateTimeOffset ahora,
        string nombreArchivo,
        string contentType,
        Stream contenido,
        CancellationToken cancellationToken)
    {
        var ext = Path.GetExtension(nombreArchivo);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".bin";

        var fechaUtc = ahora.UtcDateTime;
        var relPath = $"cxp/{fechaUtc:yyyy}/{fechaUtc:MM}/evidencias/{documentoId}/{evidenciaId}{ext}";
        var fullPath = Path.Combine(_options.RootPath, relPath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (var fs = File.Create(fullPath))
        {
            await contenido.CopyToAsync(fs, cancellationToken);
        }

        _logger.LogDebug("[LocalFilesystemEvidenciaBlobStorage] Guardado {Path}", relPath);
        return relPath;
    }
}
