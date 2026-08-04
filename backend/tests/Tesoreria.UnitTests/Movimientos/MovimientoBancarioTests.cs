using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.UnitTests.Movimientos;

public class MovimientoBancarioTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly FechaValor = new(2026, 7, 15);

    private static CuentaBancaria CuentaMxn() =>
        new(Guid.NewGuid(), "BBVA", "0123456789", null, "MXN");

    private static MovimientoBancario RegistrarIngreso(
        CuentaBancaria? cuenta = null,
        decimal monto = 1_500m,
        string? referencia = null,
        Guid? conceptoId = null,
        BeneficiarioTipo? beneficiarioTipo = null,
        Guid? beneficiarioRef = null,
        Guid? creadoPor = null) =>
        MovimientoBancario.RegistrarIngreso(
            empresaId: Guid.NewGuid(),
            cuenta: cuenta ?? CuentaMxn(),
            monto: monto,
            fechaValor: FechaValor,
            referenciaBancaria: referencia,
            conceptoId: conceptoId,
            beneficiarioTipo: beneficiarioTipo,
            beneficiarioRef: beneficiarioRef,
            creadoPor: creadoPor ?? Guid.NewGuid(),
            ahora: Ahora);

    [Fact]
    public void RegistrarIngreso_valido_OK()
    {
        var cuenta = CuentaMxn();
        var m = RegistrarIngreso(cuenta, referencia: "  spei-00123 ", beneficiarioTipo: BeneficiarioTipo.Cliente, beneficiarioRef: Guid.NewGuid());

        m.Id.Should().NotBeEmpty();
        m.CuentaBancariaId.Should().Be(cuenta.Id);
        m.Sentido.Should().Be(SentidoMovimiento.Ingreso);
        m.Monto.Should().Be(1_500m);
        m.FechaValor.Should().Be(FechaValor);
        m.EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.NoAplicado);
        m.EstadoConciliacion.Should().Be(EstadoConciliacionMovimiento.NoConciliado);
        m.ContramovimientoDe.Should().BeNull();
        m.CreadoEn.Should().Be(Ahora);
    }

    [Fact]
    public void RegistrarIngreso_copia_la_moneda_de_la_cuenta_RN3()
    {
        // RN-3: la moneda no se acepta del caller — se copia de la cuenta,
        // lo que hace imposible el cross-moneda en el MVP.
        var cuentaUsd = new CuentaBancaria(Guid.NewGuid(), "Banorte", "9876543210", null, "USD");

        var m = RegistrarIngreso(cuentaUsd);

        m.Moneda.Should().Be("USD");
    }

    [Fact]
    public void RegistrarIngreso_normaliza_referencia_trim_upper()
    {
        RegistrarIngreso(referencia: "  spei-00123 ").ReferenciaBancaria.Should().Be("SPEI-00123");
        RegistrarIngreso(referencia: "   ").ReferenciaBancaria.Should().BeNull();
        RegistrarIngreso(referencia: null).ReferenciaBancaria.Should().BeNull();
    }

    [Fact]
    public void RegistrarIngreso_cuenta_inactiva_truena()
    {
        var cuenta = CuentaMxn();
        cuenta.Desactivar();

        var act = () => RegistrarIngreso(cuenta);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("MOV_CUENTA_INACTIVA");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void RegistrarIngreso_monto_no_positivo_truena(decimal monto)
    {
        var act = () => RegistrarIngreso(monto: monto);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("MOV_MONTO_INVALIDO");
    }

    [Fact]
    public void RegistrarIngreso_beneficiario_ref_sin_tipo_truena()
    {
        var act = () => RegistrarIngreso(beneficiarioRef: Guid.NewGuid());

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("MOV_BENEFICIARIO_SIN_TIPO");
    }

    [Fact]
    public void RegistrarIngreso_sin_usuario_truena()
    {
        var act = () => RegistrarIngreso(creadoPor: Guid.Empty);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("MOV_USUARIO_VACIO");
    }
}
