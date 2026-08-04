using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Pasivos;

namespace Millet.Tesoreria.UnitTests.Movimientos;

/// <summary>
/// Tests del dominio de pago a proveedor (TES-PR4): egreso Aplicado,
/// contramovimiento RN-10 y saldo local de la proyección.
/// </summary>
public sealed class PagoProveedorDomainTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Fecha = new(2026, 7, 15);

    private static CuentaBancaria CuentaMxn() =>
        new(Guid.NewGuid(), "BBVA", "0123456789", null, "MXN");

    private static MovimientoBancario Pago(CuentaBancaria? cuenta = null, decimal monto = 10_000m) =>
        MovimientoBancario.RegistrarPagoProveedor(
            Guid.NewGuid(), cuenta ?? CuentaMxn(), Guid.NewGuid(), monto,
            Fecha, "spei-001", null, Guid.NewGuid(), Ahora);

    // --------------------------------------------------- RegistrarPagoProveedor

    [Fact]
    public void RegistrarPagoProveedor_nace_egreso_aplicado_a_proveedor()
    {
        var m = Pago();

        m.Sentido.Should().Be(SentidoMovimiento.Egreso);
        m.EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.Aplicado);
        m.BeneficiarioTipo.Should().Be(BeneficiarioTipo.Proveedor);
        m.BeneficiarioRef.Should().NotBeNull();
        m.Moneda.Should().Be("MXN");
        m.ReferenciaBancaria.Should().Be("SPEI-001");
    }

    [Fact]
    public void RegistrarPagoProveedor_cuenta_inactiva_truena()
    {
        var cuenta = CuentaMxn();
        cuenta.Desactivar();

        var act = () => Pago(cuenta);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("MOV_CUENTA_INACTIVA");
    }

    // --------------------------------------------------- Contramovimiento RN-10

    [Fact]
    public void CrearContramovimiento_invierte_sentido_y_liga_al_original()
    {
        var original = Pago(monto: 10_000m);

        var reversa = original.CrearContramovimiento(4_000m, Fecha.AddDays(1), Guid.NewGuid(), Ahora);

        reversa.Sentido.Should().Be(SentidoMovimiento.Ingreso);
        reversa.Monto.Should().Be(4_000m);
        reversa.ContramovimientoDe.Should().Be(original.Id);
        reversa.CuentaBancariaId.Should().Be(original.CuentaBancariaId);
        reversa.Moneda.Should().Be(original.Moneda);
        reversa.ReferenciaBancaria.Should().Be("REVERSA SPEI-001");
        // El original no se toca (RN-10: nada se borra ni muta).
        original.EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.Aplicado);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void CrearContramovimiento_importe_invalido_truena(decimal importe)
    {
        var act = () => Pago(monto: 10_000m)
            .CrearContramovimiento(importe, Fecha, Guid.NewGuid(), Ahora);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("MOV_REVERSA_IMPORTE_INVALIDO");
    }

    [Fact]
    public void CrearContramovimiento_de_un_contramovimiento_truena()
    {
        var reversa = Pago(monto: 10_000m)
            .CrearContramovimiento(10_000m, Fecha, Guid.NewGuid(), Ahora);

        var act = () => reversa.CrearContramovimiento(10_000m, Fecha, Guid.NewGuid(), Ahora);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("MOV_REVERSA_DE_REVERSA");
    }

    // --------------------------------------------------- AplicacionPagoProveedor

    [Fact]
    public void Aplicacion_Revertir_marca_y_no_permite_doble_reversa()
    {
        var aplicacion = new AplicacionPagoProveedor(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1_000m, Ahora);

        aplicacion.Revertir();
        aplicacion.Revertida.Should().BeTrue();

        var act = aplicacion.Revertir;
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("APL_YA_REVERTIDA");
    }

    // --------------------------------------------------- Saldo local del pasivo

    private static PasivoPendientePago Pasivo(decimal monto = 10_000m, decimal saldo = 10_000m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            monto, saldo, "MXN", null, new DateOnly(2026, 8, 1), null, null, Ahora);

    [Fact]
    public void AplicarPago_descuenta_saldo_local()
    {
        var pasivo = Pasivo();

        pasivo.AplicarPago(4_000m);

        pasivo.SaldoPendiente.Should().Be(6_000m);
    }

    [Fact]
    public void AplicarPago_que_excede_saldo_truena()
    {
        var act = () => Pasivo(saldo: 3_000m).AplicarPago(3_000.01m);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("PAGO_EXCEDE_SALDO");
    }

    [Fact]
    public void RevertirPago_restaura_saldo_con_tope_en_monto_total()
    {
        var pasivo = Pasivo(monto: 10_000m, saldo: 2_000m);

        pasivo.RevertirPago(9_000m);

        pasivo.SaldoPendiente.Should().Be(10_000m); // tope en MontoTotal
    }
}
