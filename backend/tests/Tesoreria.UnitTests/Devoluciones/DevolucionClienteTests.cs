using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Devoluciones;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.UnitTests.Devoluciones;

public sealed class DevolucionClienteTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid Solicitante = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly CuentaDestinoDevolucion Destino = new("BBVA", "032180000118359719", "Cliente SA");

    private static CuentaBancaria Cuenta() => new(EmpresaId, "BBVA", "0123456789", clabe: null, moneda: "MXN");

    private static MovimientoBancario Deposito(decimal monto = 1_000m) =>
        MovimientoBancario.RegistrarIngreso(EmpresaId, Cuenta(), monto, new DateOnly(2026, 10, 17), null, null, null, null, Solicitante, Ahora);

    private static DevolucionCliente SolicitudDeposito(decimal importe = 500m, decimal disponible = 1_000m) =>
        DevolucionCliente.SolicitarPorDepositoErroneo(EmpresaId, Deposito(), ClienteId, importe, disponible, "Depósito equivocado", Destino, Solicitante, Ahora);

    private static DevolucionCliente SolicitudNc() =>
        DevolucionCliente.SolicitarPorNotaCredito(EmpresaId, Guid.NewGuid(), "NC-1", null, ClienteId, 100m, 100m, "mxn", null, "Bonificación", null, Ahora);

    private static void Debe(Action act, string code) =>
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be(code);

    [Fact]
    public void Importe_mayor_a_lo_disponible_lanza() =>
        Debe(() => SolicitudDeposito(importe: 600m, disponible: 500m), "DEV_IMPORTE_EXCEDE_DEPOSITO");

    [Fact]
    public void Importe_igual_a_lo_disponible_pasa() =>
        SolicitudDeposito(importe: 500m, disponible: 500m).Estado.Should().Be(EstadoDevolucionCliente.Solicitada);

    [Fact]
    public void Nc_con_importe_mayor_al_total_lanza() =>
        Debe(() => DevolucionCliente.SolicitarPorNotaCredito(EmpresaId, Guid.NewGuid(), "NC-1", null, ClienteId, 101m, 100m, "MXN", null, "x", null, Ahora),
            "DEV_IMPORTE_EXCEDE_NC");

    [Fact]
    public void Misma_persona_no_autoriza() =>
        Debe(() => SolicitudDeposito().Autorizar(Solicitante, Ahora), "DEV_MISMO_USUARIO");

    [Fact]
    public void Otra_persona_autoriza()
    {
        var d = SolicitudDeposito();
        d.Autorizar(Guid.NewGuid(), Ahora);
        d.Estado.Should().Be(EstadoDevolucionCliente.Autorizada);
    }

    [Fact]
    public void Autorizar_nc_sin_destino_lanza() =>
        Debe(() => SolicitudNc().Autorizar(Guid.NewGuid(), Ahora), "DEV_DESTINO_INCOMPLETO");

    [Fact]
    public void Pagar_sin_autorizar_lanza() =>
        Debe(() => SolicitudDeposito().MarcarPagada(Guid.NewGuid(), Guid.NewGuid(), Ahora), "DEV_NO_AUTORIZADA");

    [Fact]
    public void Cancelar_una_autorizada_lanza()
    {
        var d = SolicitudDeposito();
        d.Autorizar(Guid.NewGuid(), Ahora);
        Debe(() => d.Cancelar("x", Solicitante, Ahora), "DEV_NO_PENDIENTE");
    }

    [Fact]
    public void Un_egreso_no_es_deposito()
    {
        var egreso = MovimientoBancario.RegistrarPagoProveedor(EmpresaId, Cuenta(), Guid.NewGuid(), 100m, new DateOnly(2026, 10, 17), null, null, Solicitante, Ahora);
        Debe(() => DevolucionCliente.SolicitarPorDepositoErroneo(EmpresaId, egreso, ClienteId, 50m, 100m, "x", Destino, Solicitante, Ahora),
            "DEV_DEPOSITO_NO_INGRESO");
    }
}
