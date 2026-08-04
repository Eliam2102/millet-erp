using Millet.CuentasPorPagar.Infrastructure.TarjetaCredito;

namespace Millet.CuentasPorPagar.UnitTests.TarjetaCredito;

/// <summary>
/// F7-PR5: tests del parser de Excel — solo el helper de resolución de
/// columna; el parser completo se valida con archivos sample en
/// IntegrationTests (fuera del scope MVP de unit tests).
/// </summary>
public sealed class EstadoCuentaTcExcelParserTests
{
    [Theory]
    [InlineData("A", 1)]
    [InlineData("B", 2)]
    [InlineData("Z", 26)]
    [InlineData("AA", 27)]
    [InlineData("AZ", 52)]
    [InlineData("BA", 53)]
    [InlineData("1", 1)]
    [InlineData("3", 3)]
    public void ResolverColumna_acepta_letra_y_indice(string raw, int expected)
    {
        EstadoCuentaTcExcelParser.ResolverColumna(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("@")]
    [InlineData("0")]
    public void ResolverColumna_rechaza_invalido(string raw)
    {
        var act = () => EstadoCuentaTcExcelParser.ResolverColumna(raw);
        act.Should().Throw<InvalidOperationException>();
    }
}
