using System.Globalization;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;

namespace Millet.SharedKernel.UnitTests.Application;

/// <summary>
/// Cubre <see cref="FechaContable"/> (ADR-0040 / ADR-0013). Incluye la
/// verificación de que el runtime resuelve <c>America/Mexico_City</c> vía ICU.
/// </summary>
public class FechaContableTests
{
    [Fact]
    public void ZonaOperacion_Resuelve_ViaIcu_ConOffsetDeMexico()
    {
        // Si ICU/tzdata no estuviera presente, FindSystemTimeZoneById tiraría
        // TimeZoneNotFoundException al inicializar el static. México no tiene
        // horario de verano desde 2022 → offset fijo -06:00.
        FechaContable.ZonaOperacion.Should().NotBeNull();
        FechaContable.ZonaOperacion.BaseUtcOffset.Should().Be(TimeSpan.FromHours(-6));
    }

    [Theory]
    // Medianoche exacta de México (06:00Z) → ese mismo día.
    [InlineData("2026-06-04T06:00:00Z", "2026-06-04")]
    // Un segundo antes de medianoche local → día anterior.
    [InlineData("2026-06-04T05:59:59Z", "2026-06-03")]
    // 19:00 hora de México: en UTC ya es el día siguiente, pero el día
    // operativo sigue siendo el 03. Este es el caso que DateOnly.FromDateTime(UtcNow)
    // resolvería MAL (daría el 04).
    [InlineData("2026-06-04T01:00:00Z", "2026-06-03")]
    // Mediodía de México.
    [InlineData("2026-06-04T18:00:00Z", "2026-06-04")]
    public void HoyLocal_DeInstante_DevuelveDiaCalendarioDeMexico(string instante, string esperado)
    {
        var utc = DateTimeOffset.Parse(instante, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        var hoy = FechaContable.HoyLocal(utc);

        hoy.Should().Be(DateOnly.Parse(esperado, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void HoyLocal_DeInstante_DifiereDeLaFechaUtc_TrasLas18hLocal()
    {
        // 01:00Z = 19:00 hora de México del día anterior.
        var utc = DateTimeOffset.Parse("2026-06-04T01:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        FechaContable.HoyLocal(utc).Should().Be(new DateOnly(2026, 6, 3));
        // Contraste explícito: el patrón viejo (UTC) habría dado el 04.
        DateOnly.FromDateTime(utc.UtcDateTime).Should().Be(new DateOnly(2026, 6, 4));
    }

    [Fact]
    public void InicioDelDiaUtc_DevuelveMedianocheLocalDeMexico_ComoInstanteUtc()
    {
        FechaContable.InicioDelDiaUtc(new DateOnly(2026, 6, 7))
            .Should().Be(DateTimeOffset.Parse(
                "2026-06-07T06:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    [Theory]
    [InlineData("2026-06-07")]
    [InlineData("2026-01-01")]
    [InlineData("2026-12-31")]
    public void InicioDelDiaUtc_Y_HoyLocal_SonInversas(string fecha)
    {
        var d = DateOnly.Parse(fecha, CultureInfo.InvariantCulture);

        FechaContable.HoyLocal(FechaContable.InicioDelDiaUtc(d)).Should().Be(d);
    }

    [Fact]
    public void HoyLocal_DeClock_UsaElInstanteDelReloj()
    {
        var clock = new TestClock(DateTimeOffset.Parse(
            "2026-06-04T05:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

        // 05:00Z = 23:00 hora de México del 03.
        clock.HoyLocal().Should().Be(new DateOnly(2026, 6, 3));

        clock.SetTo(DateTimeOffset.Parse(
            "2026-06-04T06:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        clock.HoyLocal().Should().Be(new DateOnly(2026, 6, 4));
    }
}
