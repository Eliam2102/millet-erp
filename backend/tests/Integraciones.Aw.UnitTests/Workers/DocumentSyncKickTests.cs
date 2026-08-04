using FluentAssertions;
using Millet.Integraciones.Aw.Application.Workers;
using Xunit;

namespace Millet.Integraciones.Aw.UnitTests.Workers;

/// <summary>
/// Tests del <see cref="DocumentSyncKick"/>: despertar temprano, timeout y
/// coalescing de kicks múltiples.
/// </summary>
public sealed class DocumentSyncKickTests
{
    [Fact]
    public async Task WaitAsync_SinKick_TimeoutDevuelveFalse()
    {
        using var kick = new DocumentSyncKick();

        var woke = await kick.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);

        woke.Should().BeFalse();
    }

    [Fact]
    public async Task Kick_DespiertaAntesDelTimeout()
    {
        using var kick = new DocumentSyncKick();

        kick.Kick();
        var woke = await kick.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        woke.Should().BeTrue();
    }

    [Fact]
    public async Task Kicks_Multiples_SeCoalescenEnUno()
    {
        using var kick = new DocumentSyncKick();

        // Tres kicks antes de consumir: solo uno queda pendiente.
        kick.Kick();
        kick.Kick();
        kick.Kick();

        (await kick.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)).Should().BeTrue();
        // El segundo wait ya no encuentra kick pendiente → timeout.
        (await kick.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None)).Should().BeFalse();
    }
}
