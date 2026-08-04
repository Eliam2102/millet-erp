using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Cuentas;

namespace Millet.Tesoreria.UnitTests.Cuentas;

public class CuentaBancariaTests
{
    private const string ClabeValida = "032180000118359719";

    [Fact]
    public void Crear_valida_OK()
    {
        var cuenta = new CuentaBancaria(
            empresaId: Guid.NewGuid(),
            banco: " BBVA México ",
            numeroCuenta: "0123456789",
            clabe: ClabeValida,
            moneda: "mxn");

        cuenta.Id.Should().NotBeEmpty();
        cuenta.Banco.Should().Be("BBVA México");
        cuenta.NumeroCuenta.Should().Be("0123456789");
        cuenta.Clabe.Should().Be(ClabeValida);
        cuenta.Moneda.Should().Be("MXN");
        cuenta.Activa.Should().BeTrue();
    }

    [Fact]
    public void Crear_sin_clabe_OK()
    {
        var cuenta = new CuentaBancaria(Guid.NewGuid(), "Banorte", "12345678", null, "USD");

        cuenta.Clabe.Should().BeNull();
    }

    [Fact]
    public void Crear_clabe_invalida_truena()
    {
        var act = () => new CuentaBancaria(
            Guid.NewGuid(), "BBVA", "12345678", "032180000118359718", "MXN");

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CLABE_DIGITO_CONTROL");
    }

    [Fact]
    public void Crear_numero_cuenta_invalido_truena()
    {
        var act = () => new CuentaBancaria(Guid.NewGuid(), "BBVA", "123", null, "MXN");

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("NUMERO_CUENTA_FORMATO");
    }

    [Fact]
    public void Crear_moneda_invalida_truena()
    {
        var act = () => new CuentaBancaria(Guid.NewGuid(), "BBVA", "12345678", null, "PESOS");

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CTA_MONEDA_INVALIDA");
    }

    [Fact]
    public void Activar_Desactivar_togglean()
    {
        var cuenta = new CuentaBancaria(Guid.NewGuid(), "BBVA", "12345678", null, "MXN");

        cuenta.Desactivar();
        cuenta.Activa.Should().BeFalse();

        cuenta.Activar();
        cuenta.Activa.Should().BeTrue();
    }

    [Fact]
    public void ActualizarDatos_normaliza_y_no_toca_numero_cuenta()
    {
        var cuenta = new CuentaBancaria(Guid.NewGuid(), "BBVA", "12345678", ClabeValida, "MXN");

        cuenta.ActualizarDatos(" Banorte ", "usd", "  1102-001  ", "  ");

        cuenta.Banco.Should().Be("Banorte");
        cuenta.Moneda.Should().Be("USD");
        cuenta.CuentaContableRef.Should().Be("1102-001");
        cuenta.PerfilExtracto.Should().BeNull();
        cuenta.NumeroCuenta.Should().Be("12345678");
        cuenta.Clabe.Should().Be(ClabeValida);
    }

    [Fact]
    public void ActualizarDatos_banco_vacio_truena()
    {
        var cuenta = new CuentaBancaria(Guid.NewGuid(), "BBVA", "12345678", null, "MXN");

        var act = () => cuenta.ActualizarDatos("  ", "MXN", null, null);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CTA_BANCO_VACIO");
    }

    [Fact]
    public void ActualizarDatos_moneda_invalida_truena()
    {
        var cuenta = new CuentaBancaria(Guid.NewGuid(), "BBVA", "12345678", null, "MXN");

        var act = () => cuenta.ActualizarDatos("BBVA", "PESOS", null, null);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CTA_MONEDA_INVALIDA");
    }

    [Fact]
    public void CambiarClabe_valida_reemplaza_y_null_limpia()
    {
        var cuenta = new CuentaBancaria(Guid.NewGuid(), "BBVA", "12345678", null, "MXN");

        cuenta.CambiarClabe(ClabeValida);
        cuenta.Clabe.Should().Be(ClabeValida);

        cuenta.CambiarClabe(null);
        cuenta.Clabe.Should().BeNull();
    }

    [Fact]
    public void CambiarClabe_invalida_truena()
    {
        var cuenta = new CuentaBancaria(Guid.NewGuid(), "BBVA", "12345678", null, "MXN");

        var act = () => cuenta.CambiarClabe("032180000118359718");

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CLABE_DIGITO_CONTROL");
    }
}
