using Millet.Administracion.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.UnitTests.Domain;

public class ToleranciaTarjetasParametroTests
{
    [Theory]
    [InlineData("1.00", 1.00)]
    [InlineData("0", 0)]
    [InlineData("2.5", 2.5)]
    public void Should_Read_Valid_Values(string valor, double esperado)
    {
        ToleranciaTarjetasParametro.LeerValor(valor).Should().Be((decimal)esperado);
    }

    [Theory]
    [InlineData("-1", "CXP_TOLERANCIA_TARJETAS_INVALIDA")]
    [InlineData("abc", "CXP_TOLERANCIA_TARJETAS_INVALIDA")]
    [InlineData("1,5", "CXP_TOLERANCIA_TARJETAS_INVALIDA")]
    [InlineData("0.00001", "CXP_TOLERANCIA_TARJETAS_PRECISION")]
    public void Should_Reject_Invalid_Values(string valor, string codigo)
    {
        var act = () => ToleranciaTarjetasParametro.LeerValor(valor);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be(codigo);
    }

    [Fact]
    public void Should_Reject_Invalid_Update_For_Tarjetas_Key()
    {
        var p = ParametroGlobal.CrearDeModulo(Guid.NewGuid(), ToleranciaTarjetasParametro.Clave, "1.00",
            TipoParametro.Numero, "cxp", "Tolerancia de tarjetas");
        var act = () => p.ActualizarValor("-1");
        act.Should().Throw<BusinessRuleException>();
        p.Valor.Should().Be("1.00");
    }
}
