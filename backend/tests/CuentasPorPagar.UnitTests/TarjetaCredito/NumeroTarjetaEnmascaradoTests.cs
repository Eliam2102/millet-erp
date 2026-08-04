using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.TarjetaCredito;

public sealed class NumeroTarjetaEnmascaradoTests
{
    [Fact]
    public void FromUltimosCuatro_genera_formato_canonico()
    {
        var n = NumeroTarjetaEnmascarado.FromUltimosCuatro("1234");
        n.Valor.Should().Be("**** **** **** 1234");
        n.UltimosCuatro.Should().Be("1234");
    }

    [Theory]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("abcd")]
    [InlineData("")]
    public void FromUltimosCuatro_rechaza_input_invalido(string raw)
    {
        var act = () => NumeroTarjetaEnmascarado.FromUltimosCuatro(raw);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_ULTIMOS_CUATRO_INVALIDO");
    }

    [Fact]
    public void Parse_reconstruye_desde_string()
    {
        var n = NumeroTarjetaEnmascarado.Parse("**** **** **** 9876");
        n.UltimosCuatro.Should().Be("9876");
    }

    [Fact]
    public void Parse_rechaza_formato_invalido()
    {
        var act = () => NumeroTarjetaEnmascarado.Parse("1234 5678 9012 3456");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_ENMASCARADO_FORMATO_INVALIDO");
    }
}
