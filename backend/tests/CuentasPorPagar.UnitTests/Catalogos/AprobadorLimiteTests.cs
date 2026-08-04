using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.Catalogos;

public sealed class AprobadorLimiteTests
{
    private static AprobadorLimite Crear(
        decimal monto = 10000m,
        DateOnly? desde = null,
        DateOnly? hasta = null) =>
        AprobadorLimite.Crear(
            empresaId: Guid.NewGuid(),
            empleadoId: Guid.NewGuid(),
            tipoGasto: TipoGastoAprobador.ReembolsoCajaChica,
            montoMax: monto,
            moneda: "MXN",
            vigenciaDesde: desde ?? new DateOnly(2026, 1, 1),
            vigenciaHasta: hasta);

    [Fact]
    public void Crear_acepta_parametros_validos()
    {
        var a = Crear();
        a.MontoMax.Should().Be(10000m);
        a.TipoGasto.Should().Be(TipoGastoAprobador.ReembolsoCajaChica);
    }

    [Fact]
    public void Crear_rechaza_monto_no_positivo()
    {
        var act = () => Crear(monto: 0m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "APROBADOR_MONTO_INVALIDO");
    }

    [Fact]
    public void Crear_rechaza_vigencia_invertida()
    {
        var act = () => Crear(
            desde: new DateOnly(2026, 6, 1),
            hasta: new DateOnly(2026, 1, 1));
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "APROBADOR_VIGENCIA_INVALIDA");
    }

    [Fact]
    public void PuedeAutorizar_true_dentro_de_limite_y_vigencia()
    {
        var a = Crear(monto: 10000m, desde: new DateOnly(2026, 1, 1));
        a.PuedeAutorizar(monto: 5000m, fecha: new DateOnly(2026, 5, 1)).Should().BeTrue();
    }

    [Fact]
    public void PuedeAutorizar_false_si_monto_excede_limite()
    {
        var a = Crear(monto: 10000m);
        a.PuedeAutorizar(monto: 15000m, fecha: new DateOnly(2026, 5, 1)).Should().BeFalse();
    }

    [Fact]
    public void PuedeAutorizar_false_si_vigencia_no_aplica()
    {
        var a = Crear(
            monto: 10000m,
            desde: new DateOnly(2026, 1, 1),
            hasta: new DateOnly(2026, 6, 30));
        a.PuedeAutorizar(monto: 1000m, fecha: new DateOnly(2026, 7, 1)).Should().BeFalse();
        a.PuedeAutorizar(monto: 1000m, fecha: new DateOnly(2025, 12, 31)).Should().BeFalse();
    }

    [Fact]
    public void Cerrar_actualiza_vigencia_hasta()
    {
        var a = Crear(desde: new DateOnly(2026, 1, 1));
        a.Cerrar(new DateOnly(2026, 6, 30));
        a.VigenciaHasta.Should().Be(new DateOnly(2026, 6, 30));
    }

    [Fact]
    public void ActualizarLimite_cambia_monto_y_moneda()
    {
        var a = Crear();
        a.ActualizarLimite(15000m, "USD");
        a.MontoMax.Should().Be(15000m);
        a.Moneda.Should().Be("USD");
    }
}
