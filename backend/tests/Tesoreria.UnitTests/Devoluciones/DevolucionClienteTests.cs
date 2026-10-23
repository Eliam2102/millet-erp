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
        DevolucionCliente.SolicitarPorDepositoErroneo(EmpresaId, "DEV-T-0001", Deposito(), ClienteId, importe, disponible, "Depósito equivocado", Destino, Solicitante, Ahora);

    private static DevolucionCliente SolicitudNc() =>
        DevolucionCliente.SolicitarPorNotaCredito(EmpresaId, "DEV-T-0001", Guid.NewGuid(), "NC-1", null, ClienteId, 100m, 100m, "mxn", null, "Bonificación", null, Ahora);

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
        Debe(() => DevolucionCliente.SolicitarPorNotaCredito(EmpresaId, "DEV-T-0001", Guid.NewGuid(), "NC-1", null, ClienteId, 101m, 100m, "MXN", null, "x", null, Ahora),
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
        Debe(() => DevolucionCliente.SolicitarPorDepositoErroneo(EmpresaId, "DEV-T-0001", egreso, ClienteId, 50m, 100m, "x", Destino, Solicitante, Ahora),
            "DEV_DEPOSITO_NO_INGRESO");
    }

    [Fact]
    public void Conserva_el_folio_recibido() =>
        SolicitudDeposito().Folio.Should().Be("DEV-T-0001");

    [Fact]
    public void Solicitante_no_rechaza_su_propia_devolucion() =>
        Debe(() => SolicitudDeposito().Rechazar("no procede", Solicitante, Ahora), "DEV_MISMO_USUARIO");

    [Fact]
    public void Editor_no_rechaza_la_devolucion_que_edito()
    {
        var d = SolicitudDeposito();
        var editor = Guid.NewGuid();
        d.Editar(ClienteId, 400m, 1_000m, "Ajuste", Destino, editor, Ahora);
        Debe(() => d.Rechazar("no procede", editor, Ahora), "DEV_MISMO_USUARIO");
    }

    [Fact]
    public void Otra_persona_rechaza()
    {
        var d = SolicitudDeposito();
        d.Rechazar("no procede", Guid.NewGuid(), Ahora);
        d.Estado.Should().Be(EstadoDevolucionCliente.Rechazada);
    }

    [Fact]
    public void Tope_con_otras_devoluciones_lo_menciona() =>
        Debe(() => SolicitudDeposito(importe: 600m, disponible: 400m), "DEV_IMPORTE_EXCEDE_DEPOSITO");

    [Fact]
    public void Tope_con_otras_devoluciones_mensaje_incluye_comprometido()
    {
        var act = () => SolicitudDeposito(importe: 600m, disponible: 400m);
        act.Should().Throw<BusinessRuleException>().Which.Message.Should().Contain("ya está comprometido en otras devoluciones");
    }

    [Fact]
    public void Tope_sin_otras_devoluciones_no_habla_de_otras()
    {
        var act = () => SolicitudDeposito(importe: 1_200m, disponible: 1_000m);
        act.Should().Throw<BusinessRuleException>().Which.Message
            .Should().NotContain("otras devoluciones").And.Contain("1,000.00");
    }
}
