using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.Catalogos;

public sealed class PoliticaViaticosTests
{
    private static PoliticaViaticos Crear(
        decimal montoMaxDia = 2000m, int diasMax = 10) =>
        PoliticaViaticos.Crear(
            empresaId: Guid.NewGuid(),
            puestoId: Guid.NewGuid(),
            tipoDestino: TipoDestinoViatico.Nacional,
            montoMaxDia: montoMaxDia,
            diasMax: diasMax,
            moneda: "MXN");

    [Fact]
    public void Crear_acepta_parametros_validos()
    {
        var p = Crear(montoMaxDia: 1500m, diasMax: 7);
        p.MontoMaxDia.Should().Be(1500m);
        p.DiasMax.Should().Be(7);
        p.Moneda.Should().Be("MXN");
        p.TipoDestino.Should().Be(TipoDestinoViatico.Nacional);
    }

    [Fact]
    public void Crear_rechaza_monto_no_positivo()
    {
        var act = () => Crear(montoMaxDia: 0m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "POLITICA_MONTO_INVALIDO");
    }

    [Fact]
    public void Crear_rechaza_dias_no_positivos()
    {
        var act = () => Crear(diasMax: 0);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "POLITICA_DIAS_INVALIDO");
    }

    [Fact]
    public void CalcularTope_devuelve_monto_max_por_dias()
    {
        var p = Crear(montoMaxDia: 2000m);
        p.CalcularTope(3).Should().Be(6000m);
    }

    [Fact]
    public void ExcedePolitica_true_cuando_monto_supera_tope()
    {
        var p = Crear(montoMaxDia: 2000m, diasMax: 10);
        p.ExcedePolitica(montoSolicitado: 7000m, diasEstimados: 3).Should().BeTrue();
    }

    [Fact]
    public void ExcedePolitica_true_cuando_dias_superan_max()
    {
        var p = Crear(montoMaxDia: 2000m, diasMax: 5);
        p.ExcedePolitica(montoSolicitado: 1000m, diasEstimados: 10).Should().BeTrue();
    }

    [Fact]
    public void ExcedePolitica_false_cuando_dentro_de_limites()
    {
        var p = Crear(montoMaxDia: 2000m, diasMax: 10);
        p.ExcedePolitica(montoSolicitado: 4000m, diasEstimados: 3).Should().BeFalse();
    }
}
