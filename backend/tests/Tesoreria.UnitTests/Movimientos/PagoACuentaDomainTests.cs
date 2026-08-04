using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.UnitTests.Movimientos;

/// <summary>
/// Tests del dominio de pago a cuenta (TES-PR6, §3.4): egreso NoAplicado
/// con motivo obligatorio y derivación del estado de aplicación (§4.2).
/// </summary>
public sealed class PagoACuentaDomainTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Fecha = new(2026, 7, 15);

    private static CuentaBancaria CuentaMxn() =>
        new(Guid.NewGuid(), "BBVA", "0123456789", null, "MXN");

    private static MovimientoBancario PagoACuenta(
        Guid? proveedorId = null, decimal monto = 5_000m, string motivo = "Pago urgente refacción") =>
        MovimientoBancario.RegistrarPagoACuenta(
            Guid.NewGuid(), CuentaMxn(), proveedorId, monto, Fecha,
            "spei-777", null, motivo, Guid.NewGuid(), Ahora);

    [Fact]
    public void RegistrarPagoACuenta_con_proveedor_nace_no_aplicado()
    {
        var proveedorId = Guid.NewGuid();
        var m = PagoACuenta(proveedorId);

        m.Sentido.Should().Be(SentidoMovimiento.Egreso);
        m.EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.NoAplicado);
        m.BeneficiarioTipo.Should().Be(BeneficiarioTipo.Proveedor);
        m.BeneficiarioRef.Should().Be(proveedorId);
        m.MotivoNoAplicado.Should().Be("Pago urgente refacción");
    }

    [Fact]
    public void RegistrarPagoACuenta_sin_proveedor_es_beneficiario_otro()
    {
        var m = PagoACuenta(proveedorId: null);

        m.BeneficiarioTipo.Should().Be(BeneficiarioTipo.Otro);
        m.BeneficiarioRef.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RegistrarPagoACuenta_sin_motivo_truena(string motivo)
    {
        var act = () => PagoACuenta(motivo: motivo);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("MOV_MOTIVO_OBLIGATORIO");
    }

    // --------------------------------------------------- Derivación §4.2

    [Theory]
    [InlineData(0, EstadoAplicacionMovimiento.NoAplicado)]
    [InlineData(2_500, EstadoAplicacionMovimiento.AplicadoParcial)]
    [InlineData(5_000, EstadoAplicacionMovimiento.Aplicado)]
    public void ActualizarEstadoAplicacion_deriva_del_acumulado(
        decimal suma, EstadoAplicacionMovimiento esperado)
    {
        var m = PagoACuenta(Guid.NewGuid(), monto: 5_000m);

        m.ActualizarEstadoAplicacion(suma);

        m.EstadoAplicacion.Should().Be(esperado);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(5_000.01)]
    public void ActualizarEstadoAplicacion_fuera_de_rango_truena(decimal suma)
    {
        var act = () => PagoACuenta(monto: 5_000m).ActualizarEstadoAplicacion(suma);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("MOV_APLICACION_EXCEDE_MONTO");
    }
}
