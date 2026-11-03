using Millet.ProductoTerminado.Application;

namespace Millet.ProductoTerminado.UnitTests.Application;

public sealed class HoraAwTests
{
    [Fact]
    public void AUtc_ReturnsOffsetZeroWithCorrectInstant()
    {
        var utc = HoraAw.AUtc(new DateTime(2026, 10, 27, 10, 0, 0, DateTimeKind.Unspecified));

        Assert.Equal(TimeSpan.Zero, utc.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 16, 0, 0, TimeSpan.Zero), utc);
    }
}
