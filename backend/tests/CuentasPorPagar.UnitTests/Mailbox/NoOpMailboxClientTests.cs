using Microsoft.Extensions.Logging.Abstractions;
using Millet.CuentasPorPagar.Infrastructure.Mailbox;

namespace Millet.CuentasPorPagar.UnitTests.Mailbox;

public sealed class NoOpMailboxClientTests
{
    [Fact]
    public async Task ListarPendientes_devuelve_lista_vacia()
    {
        var client = new NoOpMailboxClient(NullLogger<NoOpMailboxClient>.Instance);
        var lista = await client.ListarPendientesAsync(50, CancellationToken.None);
        lista.Should().BeEmpty();
    }

    [Fact]
    public async Task MoverAProcesado_es_noop()
    {
        var client = new NoOpMailboxClient(NullLogger<NoOpMailboxClient>.Instance);
        var act = async () => await client.MoverAProcesadoAsync("msg-1", CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task MoverAFallido_es_noop()
    {
        var client = new NoOpMailboxClient(NullLogger<NoOpMailboxClient>.Instance);
        var act = async () => await client.MoverAFallidoAsync("msg-1", "razon", CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DescargarAttachment_devuelve_stream_vacio_y_no_lanza()
    {
        var client = new NoOpMailboxClient(NullLogger<NoOpMailboxClient>.Instance);
        var stream = await client.DescargarAttachmentAsync("msg-1", "att-1", CancellationToken.None);
        stream.Length.Should().Be(0);
    }
}
