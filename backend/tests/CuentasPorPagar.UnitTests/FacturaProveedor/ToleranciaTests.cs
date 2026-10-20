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
    public void MontoAbsoluto_rechaza_valor_negativo()
    {
        var act = () => Tolerancia.MontoAbsoluto(-1m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TOLERANCIA_VALOR_NEGATIVO");
    }
}
