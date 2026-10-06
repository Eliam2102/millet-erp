using Microsoft.Extensions.Options;
using Millet.SharedKernel.Application.Blob;

namespace Millet.Compartido.Infrastructure.Blob;

/// <summary>
/// Implementación de <see cref="IBlobStoragePort"/> sobre el filesystem local (dev y tests).
/// Resuelve cada clave dentro de <see cref="AdjuntosBlobStorageOptions.RootPath"/> y rechaza
/// cualquier clave que salga de esa raíz (path traversal).
/// </summary>
public sealed class LocalFilesystemBlobStoragePort : IBlobStoragePort
{
    private readonly string _root;

    public LocalFilesystemBlobStoragePort(IOptions<AdjuntosBlobStorageOptions> options)
    {
        _root = Path.GetFullPath(options.Value.RootPath);
        Directory.CreateDirectory(_root);
    }

    public async Task SubirAsync(string clave, Stream contenido, string contentType, CancellationToken cancellationToken)
    {
        var ruta = Resolver(clave);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        await using var fs = new FileStream(ruta, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await contenido.CopyToAsync(fs, cancellationToken);
    }

    public Task<Stream> ObtenerStreamAsync(string clave, CancellationToken cancellationToken)
    {
        var ruta = Resolver(clave);
        if (!File.Exists(ruta)) throw new FileNotFoundException("Blob no encontrado.", clave);
        Stream s = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(s);
    }

    public Task EliminarAsync(string clave, CancellationToken cancellationToken)
    {
        var ruta = Resolver(clave);
        if (File.Exists(ruta)) File.Delete(ruta);
        return Task.CompletedTask;
    }

    private string Resolver(string clave)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Contains('\\') || Path.IsPathRooted(clave))
            throw new ArgumentException("Clave de blob inválida.", nameof(clave));

        var ruta = Path.GetFullPath(Path.Combine(_root, clave));
        if (!ruta.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Clave de blob inválida.", nameof(clave));
        return ruta;
    }
}
