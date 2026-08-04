using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Compras.Infrastructure.Oc.Stubs;

namespace Millet.Compras.UnitTests.Oc.Infrastructure;

/// <summary>
/// Tests del <see cref="LocalFilesystemBlobStub"/>. Usa un directorio
/// temporal único por test para no contaminar otras corridas.
/// </summary>
public class LocalFilesystemBlobStubTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly LocalFilesystemBlobStub _stub;

    public LocalFilesystemBlobStubTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"millet-oc-blobs-test-{Guid.NewGuid():N}");
        var options = Options.Create(new LocalFilesystemBlobStubOptions { RootPath = _tempRoot });
        _stub = new LocalFilesystemBlobStub(options, NullLogger<LocalFilesystemBlobStub>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SubirAsync_GeneraUrlConPrefijoFile_YArchivoExiste()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        using var ms = new MemoryStream(bytes);
        var blobId = Guid.CreateVersion7();

        var url = await _stub.SubirAsync(blobId, ms, "application/pdf", "cotizacion.pdf", default);

        Assert.StartsWith("file://", url);
        Assert.Contains(blobId.ToString("D"), url);
        Assert.EndsWith(".pdf", url);
    }

    [Fact]
    public async Task ObtenerStreamAsync_DevuelveBytesEscritos()
    {
        var bytes = new byte[] { 10, 20, 30 };
        using var ms = new MemoryStream(bytes);
        var url = await _stub.SubirAsync(
            Guid.CreateVersion7(), ms, "application/octet-stream", "x.bin", default);

        await using var stream = await _stub.ObtenerStreamAsync(url, default);
        using var reader = new MemoryStream();
        await stream.CopyToAsync(reader);

        Assert.Equal(bytes, reader.ToArray());
    }

    [Fact]
    public async Task EliminarAsync_BorraElArchivo()
    {
        using var ms = new MemoryStream([42]);
        var url = await _stub.SubirAsync(
            Guid.CreateVersion7(), ms, "text/plain", "x.txt", default);

        await _stub.EliminarAsync(url, default);

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _stub.ObtenerStreamAsync(url, default));
    }

    [Fact]
    public async Task EliminarAsync_BlobInexistente_NoLanza()
    {
        var fakeUrl = $"file://{Path.Combine(_tempRoot, "no-existe.pdf")}";
        // Idempotente — no debe lanzar.
        await _stub.EliminarAsync(fakeUrl, default);
    }

    [Fact]
    public async Task SubirAsync_ExtensionInferidaDelContentType()
    {
        using var ms = new MemoryStream([1]);
        // Nombre sin extensión → inferir del content type.
        var url = await _stub.SubirAsync(
            Guid.CreateVersion7(), ms, "application/pdf", "sin-extension", default);

        Assert.EndsWith(".pdf", url);
    }
}
