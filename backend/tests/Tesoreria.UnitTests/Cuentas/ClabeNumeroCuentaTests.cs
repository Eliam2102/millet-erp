using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Cuentas;

namespace Millet.Tesoreria.UnitTests.Cuentas;

public class ClabeTests
{
    // CLABE con dígito verificador correcto (ponderación Banxico 3-7-1).
    private const string ClabeValida = "032180000118359719";

    [Fact]
    public void Crear_valida_OK()
    {
        var clabe = Clabe.Crear(ClabeValida);

        clabe.Valor.Should().Be(ClabeValida);
    }

    [Fact]
    public void Crear_con_espacios_hace_trim()
    {
        Clabe.Crear($"  {ClabeValida} ").Valor.Should().Be(ClabeValida);
    }

    [Theory]
    [InlineData("032180000118359718")] // dígito de control incorrecto
    [InlineData("032180000118359710")]
    public void Crear_digito_control_invalido_truena(string valor)
    {
        var act = () => Clabe.Crear(valor);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CLABE_DIGITO_CONTROL");
    }

    [Theory]
    [InlineData("12345")]                 // corta
    [InlineData("0321800001183597190")]   // larga (19)
    [InlineData("03218000011835971A")]    // no numérica
    public void Crear_formato_invalido_truena(string valor)
    {
        var act = () => Clabe.Crear(valor);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CLABE_FORMATO");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Crear_vacia_truena(string valor)
    {
        var act = () => Clabe.Crear(valor);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CLABE_VACIA");
    }

    [Fact]
    public void ToString_y_Enmascarada_solo_muestran_ultimos_4()
    {
        var clabe = Clabe.Crear(ClabeValida);

        clabe.Enmascarada.Should().Be("**************9719");
        clabe.ToString().Should().Be("**************9719");
    }

    [Fact]
    public void Enmascarar_valores_cortos_enmascara_completo()
    {
        Clabe.Enmascarar("1234").Should().Be("****");
        Clabe.Enmascarar("12").Should().Be("**");
    }
}

public class NumeroCuentaTests
{
    [Theory]
    [InlineData("12345678")]
    [InlineData("ES9121000418450200051332")] // IBAN-style alfanumérico
    public void Crear_valido_OK(string valor)
    {
        NumeroCuenta.Crear(valor).Valor.Should().Be(valor);
    }

    [Theory]
    [InlineData("12345")]      // < 6
    [InlineData("1234 5678")]  // espacio interno
    [InlineData("1234-5678")]  // guion
    public void Crear_formato_invalido_truena(string valor)
    {
        var act = () => NumeroCuenta.Crear(valor);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("NUMERO_CUENTA_FORMATO");
    }

    [Fact]
    public void ToString_enmascara()
    {
        NumeroCuenta.Crear("12345678").ToString().Should().Be("****5678");
    }
}
