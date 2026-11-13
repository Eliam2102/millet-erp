using Millet.SharedKernel.Application.Integration;

namespace Millet.SharedKernel.UnitTests.Outbox;

/// <summary>I13.6 · espera creciente entre reintentos de una integración.</summary>
public sealed class EsperaReintentoTests
{
    private static readonly TimeSpan Base = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Tope = TimeSpan.FromMinutes(30);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(4, 40)]
    [InlineData(9, 1280)]
    [InlineData(10, 1800)]
    [InlineData(500, 1800)]
    public void Se_duplica_en_cada_falla_sin_pasar_del_tope(int fallas, int segundos) =>
        EsperaReintento.Calcular(fallas, Base, Tope).Should().Be(TimeSpan.FromSeconds(segundos));

    [Fact]
    public void Con_los_valores_por_omision_del_outbox_aguanta_una_caida_de_una_hora()
    {
        // MaxAttempts 10 admite 11 intentos: 10 esperas entre ellos.
        EsperaReintento.Total(11, Base, Tope).Should().BeGreaterThan(TimeSpan.FromHours(1));
    }

    [Fact]
    public void Sin_base_no_hay_espera() =>
        EsperaReintento.Calcular(3, TimeSpan.Zero, Tope).Should().Be(TimeSpan.Zero);
}
