using Millet.Almacen.Application.Cierre;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Periodos;

public sealed class CierrePeriodoFuturoTests
{
    [Fact]
    public async Task Cierre_de_periodo_futuro_lanza_CIERRE_PERIODO_FUTURO()
    {
        await using var f = new P1Fixture();
        var handler = new EjecutarCierreMensualHandler(f.Db, f.Context, f.Context);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => handler.Handle(new(2099, 12), default));

        ex.Code.Should().Be("CIERRE_PERIODO_FUTURO");
    }

    [Fact]
    public async Task Cierre_valido_de_mes_concluido_devuelve_anio_y_mes()
    {
        await using var f = new P1Fixture();
        var handler = new EjecutarCierreMensualHandler(f.Db, f.Context, f.Context);
        var mesConcluido = FechaNegocio.Hoy(DateTimeOffset.UtcNow).AddMonths(-1);

        var respuesta = await handler.Handle(new(mesConcluido.Year, mesConcluido.Month), default);

        respuesta.Anio.Should().Be(mesConcluido.Year);
        respuesta.Mes.Should().Be(mesConcluido.Month);
    }

    [Fact]
    public void FechaNegocio_usa_el_dia_local_de_mexico_City()
    {
        // 2026-10-01 03:00 UTC = 2026-09-30 21:00 en America/Mexico_City (UTC-6, sin horario de verano).
        var hoy = FechaNegocio.Hoy(new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero));

        hoy.Should().Be(new DateOnly(2026, 9, 30));
    }
}
