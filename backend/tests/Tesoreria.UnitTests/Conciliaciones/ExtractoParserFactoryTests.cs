using FluentAssertions;
using Millet.Tesoreria.Domain.Conciliaciones.ExtractoParser;

namespace Millet.Tesoreria.UnitTests.Conciliaciones;

public class ExtractoParserFactoryTests
{
    private readonly ExtractoParserFactory _factory = new();

    [Theory]
    [InlineData("BBVA")]
    [InlineData("bbva")]
    [InlineData("GENERICO")]
    [InlineData("CSV")]
    public void ObtenerParser_con_perfiles_bbva_o_generico_retorna_BbvaExtractoParser(string perfil)
    {
        var parser = _factory.ObtenerParser(perfil);
        parser.Should().BeOfType<BbvaExtractoParser>();
        parser.PerfilBanco.Should().Be("BBVA");
    }

    [Theory]
    [InlineData("SANTANDER")]
    [InlineData("santander")]
    [InlineData("GETNET")]
    [InlineData("getnet")]
    public void ObtenerParser_con_perfil_santander_o_getnet_retorna_SantanderExtractoParser(string perfil)
    {
        var parser = _factory.ObtenerParser(perfil);
        parser.Should().BeOfType<SantanderExtractoParser>();
        parser.PerfilBanco.Should().Be("SANTANDER");
    }

    [Fact]
    public void ObtenerParser_sin_perfil_detecta_santander_por_cabecera_cargo_abono()
    {
        string contenidoSantander =
            "FECHA,CONCEPTO,REFERENCIA,CARGO/ABONO,SALDO\n" +
            "01102026,DEPOSITO TERMINAL,AUT123,+15000.00,100000.00\n";

        var parser = _factory.ObtenerParser(null, contenidoSantander);
        parser.Should().BeOfType<SantanderExtractoParser>();
    }

    [Fact]
    public void ObtenerParser_sin_perfil_detecta_bbva_por_formato_10_columnas()
    {
        string contenidoBbva10 =
            "01/10/2026,01/10/2026,Y16,DEPOSITO CLIENTE,REF001,0000000000.00,0000010000.00,0000050000.00,2773,MXP\n";

        var parser = _factory.ObtenerParser(null, contenidoBbva10);
        parser.Should().BeOfType<BbvaExtractoParser>();
    }

    [Fact]
    public void ObtenerParser_con_formato_default_bbva_pero_archivo_getnet_detecta_santander()
    {
        string contenidoSantander =
            "FECHA,CONCEPTO,REFERENCIA,CARGO/ABONO,SALDO\n" +
            "01102026,LIQUIDACION GETNET,AUT123,+15000.00,100000.00\n";

        var parser = _factory.ObtenerParser("BBVA", contenidoSantander);
        parser.Should().BeOfType<SantanderExtractoParser>();
    }
}
