using Microsoft.Extensions.Options;
using Millet.Compartido.Infrastructure.Blob;

namespace Millet.Compartido.UnitTests.Infrastructure.Blob;

public sealed class LocalFilesystemBlobStoragePortTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "millet-adj-test-" + Guid.NewGuid().ToString("N"));
    private readonly LocalFilesystemBlobStoragePort _port;

    public LocalFilesystemBlobStoragePortTests() =>
        _port = new(Options.Create(new AdjuntosBlobStorageOptions { RootPath = _root }));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Subir_y_leer_devuelve_el_mismo_contenido()
    {
        await _port.SubirAsync("proveedor/e1/a1.pdf", new MemoryStream("hola"u8.ToArray()), "application/pdf", default);

        await using var s = await _port.ObtenerStreamAsync("proveedor/e1/a1.pdf", default);
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms);
        ms.ToArray().Should().Equal("hola"u8.ToArray());
    }

    [Fact]
    public async Task Eliminar_es_idempotente()
    {
        await _port.SubirAsync("proveedor/e1/a2.pdf", new MemoryStream([1]), "application/pdf", default);

        await _port.EliminarAsync("proveedor/e1/a2.pdf", default);
        await _port.EliminarAsync("proveedor/e1/a2.pdf", default);

        await _port.Invoking(p => p.ObtenerStreamAsync("proveedor/e1/a2.pdf", default))
            .Should().ThrowAsync<FileNotFoundException>();
    }

    [Theory]
    [InlineData("../fuera.pdf")]
    [InlineData("proveedor/../../fuera.pdf")]
    [InlineData("/etc/passwd")]
    [InlineData("a\\b.pdf")]
    [InlineData("")]
    public async Task Clave_con_traversal_o_invalida_se_rechaza(string clave)
    {
        await _port.Invoking(p => p.SubirAsync(clave, new MemoryStream([1]), "application/pdf", default))
            .Should().ThrowAsync<ArgumentException>();
        await _port.Invoking(p => p.EliminarAsync(clave, default)).Should().ThrowAsync<ArgumentException>();
    }
}
