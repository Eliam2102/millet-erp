using Millet.CuentasPorPagar.Application.FacturaProveedor.Revision;
using Millet.CuentasPorPagar.Domain.Notificaciones;
using Millet.SharedKernel.Application.Calendario;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

public sealed class PlazoRevisionTests
{
    private static readonly DateTimeOffset Entrada = DateTimeOffset.Parse("2026-10-16T21:00:00Z");

    [Fact]
    public void Umbrales_por_plazo()
    {
        PlazoRevision.Umbrales(5).Should().Be((3, 5, 10));
        PlazoRevision.Umbrales(15).Should().Be((8, 12, 20));
    }

    [Theory]
    [InlineData(4, null, null)]
    [InlineData(2, 5, null)]
    [InlineData(3, 5, NivelEscalamientoSla.AvisoGerente)]
    [InlineData(5, 5, NivelEscalamientoSla.SlaVencido)]
    [InlineData(10, 5, NivelEscalamientoSla.EscalamientoDirector)]
    [InlineData(7, 15, null)]
    [InlineData(8, 15, NivelEscalamientoSla.AvisoGerente)]
    [InlineData(12, 15, NivelEscalamientoSla.SlaVencido)]
    [InlineData(20, 15, NivelEscalamientoSla.EscalamientoDirector)]
    public void Nivel_por_dias_habiles(int dias, int? sla, NivelEscalamientoSla? esperado) =>
        PlazoRevision.Nivel(dias, sla).Should().Be(esperado);

    [Theory]
    [InlineData("2026-10-18T18:00:00Z", 0)]
    [InlineData("2026-10-21T18:00:00Z", 3)]
    [InlineData("2026-10-23T15:00:00Z", 5)]
    [InlineData("2026-10-28T18:00:00Z", 8)]
    [InlineData("2026-11-03T18:00:00Z", 12)]
    [InlineData("2026-11-13T18:00:00Z", 20)]
    public async Task Dias_habiles_desde_viernes(string ahora, int esperado)
    {
        var dias = await PlazoRevision.DiasHabilesAsync(
            new Calendario(), Entrada, DateTimeOffset.Parse(ahora), new Dictionary<DateOnly, int>(), default);
        dias.Should().Be(esperado);
    }

    private sealed class Calendario : ICalendarioHabil
    {
        public Task<int> ContarDiasAsync(DateOnly desde, DateOnly hasta, CancellationToken ct) =>
            Task.FromResult(CalendarioHabil.ContarDias(desde, hasta, CalendarioHabil.LeerFestivos(CalendarioHabil.FestivosIniciales)));

        public Task<DateTimeOffset> SumarHorasAsync(DateOnly fecha, int horas, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
