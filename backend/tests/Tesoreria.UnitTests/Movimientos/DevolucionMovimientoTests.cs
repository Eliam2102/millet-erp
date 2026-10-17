using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.UnitTests.Movimientos;

public class DevolucionMovimientoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Fecha = new(2026, 10, 17);
    private static readonly Guid Cliente = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly CuentaDestinoPago Destino = new(Guid.NewGuid(), "BBVA", "032180000118359719", "Cliente SA");

    private static MovimientoBancario Deposito(CuentaBancaria cuenta) =>
        MovimientoBancario.RegistrarIngreso(Guid.NewGuid(), cuenta, 1_000m, Fecha, null, null, null, null, Usuario, Ahora);

    [Fact]
    public void CrearDevolucionDeposito_genera_contramovimiento_ligado_sin_tocar_el_deposito()
    {
        var cuenta = new CuentaBancaria(Guid.NewGuid(), "BBVA", "0123456789", null, "MXN");
        var dep = Deposito(cuenta);
        var egreso = dep.CrearDevolucionDeposito(400m, Fecha, Cliente, Destino, null, true, Usuario, Ahora, " error de cliente ");
        egreso.Sentido.Should().Be(SentidoMovimiento.Egreso);
        egreso.Monto.Should().Be(400m);
        egreso.ContramovimientoDe.Should().Be(dep.Id);
        egreso.BeneficiarioTipo.Should().Be(BeneficiarioTipo.Cliente);
        egreso.MotivoReversa.Should().Be("error de cliente");
        egreso.CuentaBancariaId.Should().Be(dep.CuentaBancariaId);
        egreso.Moneda.Should().Be(dep.Moneda);
        egreso.FondosVerificados.Should().BeTrue();
        dep.Monto.Should().Be(1_000m);
    }

    [Fact]
    public void CrearDevolucionDeposito_sobre_egreso_lanza()
    {
        var cuenta = new CuentaBancaria(Guid.NewGuid(), "BBVA", "0123456789", null, "MXN");
        var egreso = Deposito(cuenta).CrearDevolucionDeposito(400m, Fecha, Cliente, Destino, null, true, Usuario, Ahora, "x");
        var act = () => egreso.CrearDevolucionDeposito(100m, Fecha, Cliente, Destino, null, true, Usuario, Ahora, "x");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "DEV_DEPOSITO_NO_INGRESO");
    }

    [Fact]
    public void CrearDevolucionDeposito_importe_mayor_lanza()
    {
        var dep = Deposito(new CuentaBancaria(Guid.NewGuid(), "BBVA", "0123456789", null, "MXN"));
        var act = () => dep.CrearDevolucionDeposito(1_001m, Fecha, Cliente, Destino, null, true, Usuario, Ahora, "x");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "MOV_REVERSA_IMPORTE_INVALIDO");
    }

    [Fact]
    public void RegistrarDevolucionCliente_cuenta_inactiva_lanza()
    {
        var cuenta = new CuentaBancaria(Guid.NewGuid(), "BBVA", "0123456789", null, "MXN");
        cuenta.Desactivar();
        var act = () => MovimientoBancario.RegistrarDevolucionCliente(Guid.NewGuid(), cuenta, Cliente, 100m, Fecha, null, Destino, true, Usuario, Ahora, null);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "MOV_CUENTA_INACTIVA");
    }
}
