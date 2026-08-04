using Millet.Compras.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Domain;

public class FolioTests
{
    [Theory]
    [InlineData("MID2026-000001")]
    [InlineData("MX2026-000042")]
    [InlineData("CAN2026-999999")]
    [InlineData("MERI2026-000001")]
    public void Should_ParseFolio_When_FormatIsValid(string valor)
    {
        var folio = Folio.Parse(valor);

        Assert.Equal(valor, folio.Valor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("M2026-000001")]            // prefijo de 1 letra (mínimo 2)
    [InlineData("MERIDA2026-000001")]       // prefijo de 6 letras (máximo 4)
    [InlineData("mid2026-000001")]          // prefijo en minúsculas
    [InlineData("MID26-000001")]            // año de 2 dígitos
    [InlineData("MID2026000001")]           // sin guión
    [InlineData("MID2026-1")]               // secuencial sin padding (1 dígito)
    [InlineData("MID2026-0000001")]         // secuencial de 7 dígitos
    [InlineData("MID2026-00000A")]          // secuencial con letras
    public void Should_Throw_When_FormatIsInvalid(string valor)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Folio.Parse(valor));

        Assert.Equal("FOLIO_FORMATO_INVALIDO", ex.Code);
    }

    [Fact]
    public void Should_BeEqual_When_SameValor()
    {
        var a = Folio.Parse("MID2026-000001");
        var b = Folio.Parse("MID2026-000001");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Should_NotBeEqual_When_DistinctValor()
    {
        var a = Folio.Parse("MID2026-000001");
        var b = Folio.Parse("MID2026-000002");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ToString_Should_ReturnValor()
    {
        var folio = Folio.Parse("MID2026-000001");

        Assert.Equal("MID2026-000001", folio.ToString());
    }
}
