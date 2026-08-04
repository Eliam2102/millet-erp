using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.Cfdi;

public sealed class RfcMexicanoTests
{
    [Theory]
    [InlineData("MIL010101AAA")]   // persona moral 12 chars
    [InlineData("XAXX010101000")]  // RFC genérico nacional 13 chars (extranjero)
    [InlineData("PEPP800101XYZ")]  // persona física 13 chars
    public void Parse_acepta_formatos_validos(string raw)
    {
        var rfc = RfcMexicano.Parse(raw);
        rfc.Valor.Should().Be(raw.ToUpperInvariant());
    }

    [Theory]
    [InlineData("")]
    [InlineData("MIL")]
    [InlineData("MIL010101")]
    [InlineData("MIL01010")]
    [InlineData("12345678901")]
    public void Parse_rechaza_formatos_invalidos(string raw)
    {
        var act = () => RfcMexicano.Parse(raw);
        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void Parse_normaliza_a_mayusculas()
    {
        var rfc = RfcMexicano.Parse("mil010101aaa");
        rfc.Valor.Should().Be("MIL010101AAA");
    }
}
