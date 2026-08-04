using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

public sealed class ToleranciaTests
{
    [Fact]
    public void MontoAbsoluto_pasa_si_diferencia_menor_o_igual_al_valor()
    {
        var t = Tolerancia.MontoAbsoluto(0.99m);
        t.Pasa(diferencia: 0.50m,  totalOc: 1000m).Should().BeTrue();
        t.Pasa(diferencia: 0.99m,  totalOc: 1000m).Should().BeTrue();
        t.Pasa(diferencia: -0.99m, totalOc: 1000m).Should().BeTrue("toma valor absoluto");
        t.Pasa(diferencia: 1.00m,  totalOc: 1000m).Should().BeFalse();
    }

    [Fact]
    public void Porcentaje_pasa_si_diferencia_menor_o_igual_a_porcentaje_de_oc()
    {
        var t = Tolerancia.Porcentaje(1m); // 1%
        t.Pasa(diferencia: 9.99m,  totalOc: 1000m).Should().BeTrue();
        t.Pasa(diferencia: 10.00m, totalOc: 1000m).Should().BeTrue();
        t.Pasa(diferencia: 10.01m, totalOc: 1000m).Should().BeFalse();
        t.Pasa(diferencia: -5m,    totalOc: 1000m).Should().BeTrue();
    }

    [Fact]
    public void MontoAbsoluto_rechaza_valor_negativo()
    {
        var act = () => Tolerancia.MontoAbsoluto(-1m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TOLERANCIA_VALOR_NEGATIVO");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void Porcentaje_rechaza_fuera_de_rango(decimal valor)
    {
        var act = () => Tolerancia.Porcentaje(valor);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TOLERANCIA_PORCENTAJE_FUERA_DE_RANGO");
    }
}
