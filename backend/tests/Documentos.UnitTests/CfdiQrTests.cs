using System.Globalization;

namespace Millet.Documentos.UnitTests;

public sealed class CfdiQrTests
{
    [Fact]
    public void Should_Format_Url_WithoutCultureAndEscapeValues()
    {
        var anterior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
            CfdiQr.Url("00000000-0000-0000-0000-000000000001", "AAA&010101AAA", "BBB&010101BBB", 78806.33m, "prefijoAB+/12==")
                .Should().Be("https://verificacfdi.facturaelectronica.sat.gob.mx/default.aspx?id=00000000-0000-0000-0000-000000000001&re=AAA%26010101AAA&rr=BBB%26010101BBB&tt=0000078806.330000&fe=AB%2B%2F12%3D%3D");
        }
        finally { CultureInfo.CurrentCulture = anterior; }
    }

    [Theory]
    [InlineData("0", "0000000000.000000")]
    [InlineData("0.1234565", "0000000000.123457")]
    [InlineData("9999999999.999999", "9999999999.999999")]
    public void Should_Format_Total(string importe, string esperado) =>
        CfdiQr.Url("muestra", "AAA", "BBB", decimal.Parse(importe, CultureInfo.InvariantCulture), "1234567890")
            .Should().Contain("&tt=" + esperado + "&fe=34567890");

    [Fact]
    public void Should_Generate_Png()
    {
        var png = CfdiQr.Png(CfdiQr.Url("muestra", "AAA", "BBB", 0, "12345678"));
        png.Take(8).Should().Equal(137, 80, 78, 71, 13, 10, 26, 10);
    }

    [Fact]
    public void Should_Reject_InvalidInputs()
    {
        FluentActions.Invoking(() => CfdiQr.Url("id", "AAA", "BBB", 1, "corto")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => CfdiQr.Url("id", "AAA", "BBB", -1, "12345678")).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => CfdiQr.Url("id", "AAA", "BBB", 10_000_000_000, "12345678")).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => CfdiQr.Png("url", 0)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
