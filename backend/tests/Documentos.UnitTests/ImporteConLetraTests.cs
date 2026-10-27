namespace Millet.Documentos.UnitTests;

public sealed class ImporteConLetraTests
{
    [Theory]
    [InlineData("0", "CERO PESOS 00/100 M.N.", "ZERO PESOS 00/100 M.N.")]
    [InlineData("1", "UN PESO 00/100 M.N.", "ONE PESO 00/100 M.N.")]
    [InlineData("21", "VEINTIÚN PESOS 00/100 M.N.", "TWENTY-ONE PESOS 00/100 M.N.")]
    [InlineData("22", "VEINTIDÓS PESOS 00/100 M.N.", "TWENTY-TWO PESOS 00/100 M.N.")]
    [InlineData("100", "CIEN PESOS 00/100 M.N.", "ONE HUNDRED PESOS 00/100 M.N.")]
    [InlineData("101", "CIENTO UN PESOS 00/100 M.N.", "ONE HUNDRED ONE PESOS 00/100 M.N.")]
    [InlineData("1000", "MIL PESOS 00/100 M.N.", "ONE THOUSAND PESOS 00/100 M.N.")]
    [InlineData("1001000", "UN MILLÓN MIL PESOS 00/100 M.N.", "ONE MILLION ONE THOUSAND PESOS 00/100 M.N.")]
    [InlineData("2898.04", "DOS MIL OCHOCIENTOS NOVENTA Y OCHO PESOS 04/100 M.N.", "TWO THOUSAND EIGHT HUNDRED NINETY-EIGHT PESOS 04/100 M.N.")]
    [InlineData("21000000", "VEINTIÚN MILLONES DE PESOS 00/100 M.N.", "TWENTY-ONE MILLION PESOS 00/100 M.N.")]
    [InlineData("1000000", "UN MILLÓN DE PESOS 00/100 M.N.", "ONE MILLION PESOS 00/100 M.N.")]
    [InlineData("2000000", "DOS MILLONES DE PESOS 00/100 M.N.", "TWO MILLION PESOS 00/100 M.N.")]
    [InlineData("0.5", "CERO PESOS 50/100 M.N.", "ZERO PESOS 50/100 M.N.")]
    [InlineData("0.005", "CERO PESOS 01/100 M.N.", "ZERO PESOS 01/100 M.N.")]
    [InlineData("0.004", "CERO PESOS 00/100 M.N.", "ZERO PESOS 00/100 M.N.")]
    [InlineData("0.995", "UN PESO 00/100 M.N.", "ONE PESO 00/100 M.N.")]
    [InlineData("999.995", "MIL PESOS 00/100 M.N.", "ONE THOUSAND PESOS 00/100 M.N.")]
    public void Should_Convert_Mxn(string importe, string espanol, string ingles)
    {
        var valor = decimal.Parse(importe, System.Globalization.CultureInfo.InvariantCulture);
        ImporteConLetra.Espanol(valor, "MXN").Should().Be(espanol);
        ImporteConLetra.Ingles(valor, "MXN").Should().Be(ingles);
    }

    [Theory]
    [InlineData("1", "UN DÓLAR 00/100 USD", "ONE DOLLAR 00/100 USD")]
    [InlineData("78806.33", "SETENTA Y OCHO MIL OCHOCIENTOS SEIS DÓLARES 33/100 USD", "SEVENTY-EIGHT THOUSAND EIGHT HUNDRED SIX DOLLARS 33/100 USD")]
    [InlineData("1000000000", "MIL MILLONES DE DÓLARES 00/100 USD", "ONE BILLION DOLLARS 00/100 USD")]
    [InlineData("999999999999.99", "NOVECIENTOS NOVENTA Y NUEVE MIL NOVECIENTOS NOVENTA Y NUEVE MILLONES NOVECIENTOS NOVENTA Y NUEVE MIL NOVECIENTOS NOVENTA Y NUEVE DÓLARES 99/100 USD", "NINE HUNDRED NINETY-NINE BILLION NINE HUNDRED NINETY-NINE MILLION NINE HUNDRED NINETY-NINE THOUSAND NINE HUNDRED NINETY-NINE DOLLARS 99/100 USD")]
    public void Should_Convert_Usd(string importe, string espanol, string ingles)
    {
        var valor = decimal.Parse(importe, System.Globalization.CultureInfo.InvariantCulture);
        ImporteConLetra.Espanol(valor, "USD").Should().Be(espanol);
        ImporteConLetra.Ingles(valor, "USD").Should().Be(ingles);
    }

    [Theory]
    [InlineData("-0.001")]
    [InlineData("1000000000000")]
    [InlineData("999999999999.991")]
    public void Should_Reject_OutOfRange(string importe)
    {
        var valor = decimal.Parse(importe, System.Globalization.CultureInfo.InvariantCulture);
        FluentActions.Invoking(() => ImporteConLetra.Espanol(valor, "MXN")).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ImporteConLetra.Ingles(valor, "USD")).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("EUR")]
    [InlineData("")]
    [InlineData(null)]
    public void Should_Reject_UnsupportedCurrency(string? moneda)
    {
        FluentActions.Invoking(() => ImporteConLetra.Espanol(1, moneda!)).Should().Throw<ArgumentException>().WithMessage("*MXN o USD*");
        FluentActions.Invoking(() => ImporteConLetra.Ingles(1, moneda!)).Should().Throw<ArgumentException>().WithMessage("*MXN o USD*");
    }
}
