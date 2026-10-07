using System.Security.Cryptography;

namespace Millet.Compartido.Application.Adjuntos;

/// <summary>
/// Envuelve un stream de lectura y calcula SHA-256 y bytes leídos mientras el blob port lo consume
/// (una sola pasada, sin cargar el archivo a memoria).
/// </summary>
internal sealed class HashingStream : Stream
{
    private readonly Stream _inner;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public HashingStream(Stream inner) => _inner = inner;

    public long BytesLeidos { get; private set; }

    /// <summary>Hash final en hexadecimal minúsculas (64). Llamar una sola vez, tras consumir el stream.</summary>
    public string HashHex() => Convert.ToHexStringLower(_hash.GetHashAndReset());

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var n = _inner.Read(buffer);
        Acumular(buffer[..n]);
        return n;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var n = await _inner.ReadAsync(buffer, cancellationToken);
        Acumular(buffer.Span[..n]);
        return n;
    }

    private void Acumular(ReadOnlySpan<byte> datos)
    {
        _hash.AppendData(datos);
        BytesLeidos += datos.Length;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hash.Dispose();
        base.Dispose(disposing);
    }
}
