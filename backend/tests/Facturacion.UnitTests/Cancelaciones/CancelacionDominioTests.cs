using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cancelaciones;

public sealed class CancelacionDominioTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    // ---- SolicitudCancelacion FSM ----

    private static readonly Guid Ingresos = Guid.NewGuid();
    private static readonly Guid Contador = Guid.NewGuid();

    private static SolicitudCancelacion Pendiente(string motivo = "02", string? sustituto = null) =>
        SolicitudCancelacion.Crear(Guid.NewGuid(), Guid.NewGuid(), motivo, sustituto, Ahora, Ingresos, "Ingresos");

    [Fact]
    public void Crear_arranca_pendiente_de_autorizacion_con_quien_la_solicita()
    {
        var s = Pendiente();
        s.Estado.Should().Be(EstadoSolicitudCancelacion.PendienteAutorizacion);
        (s.SolicitadaPorId, s.SolicitadaPorNombre).Should().Be((Ingresos, "Ingresos"));
        s.EstaViva.Should().BeTrue();
    }

    [Fact]
    public void Crear_motivo_01_sin_sustituto_lanza()
    {
        var act = () => SolicitudCancelacion.Crear(Guid.NewGuid(), Guid.NewGuid(), "01", null, Ahora);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CANCELACION_SUSTITUTO_REQUERIDO");
    }

    [Fact]
    public void Autorizar_la_deja_lista_para_el_PAC_y_registra_al_Contador()
    {
        var s = Pendiente("01", "22222222-2222-2222-2222-222222222222");

        s.Autorizar(Contador, "Contador", Ahora);

        s.Estado.Should().Be(EstadoSolicitudCancelacion.Solicitada);
        (s.DecididaPorId, s.DecididaPorNombre, s.DecididaEn).Should().Be((Contador, "Contador", Ahora));
    }

    [Fact]
    public void Quien_la_solicito_no_puede_autorizarla_ni_rechazarla()
    {
        var s = Pendiente();

        var autorizar = () => s.Autorizar(Ingresos, "Ingresos", Ahora);
        var rechazar = () => s.RechazarAutorizacion(Ingresos, "Ingresos", "no", Ahora);

        autorizar.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CANCELACION_AUTOAUTORIZACION");
        rechazar.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CANCELACION_AUTOAUTORIZACION");
        s.Estado.Should().Be(EstadoSolicitudCancelacion.PendienteAutorizacion);
    }

    [Fact]
    public void Rechazar_exige_motivo_y_la_resuelve()
    {
        var s = Pendiente();

        var sinMotivo = () => s.RechazarAutorizacion(Contador, "Contador", " ", Ahora);
        sinMotivo.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CANCELACION_MOTIVO_RECHAZO_REQUERIDO");

        s.RechazarAutorizacion(Contador, "Contador", " Falta la nota de crédito ", Ahora);
        s.Estado.Should().Be(EstadoSolicitudCancelacion.RechazadaPorAutorizador);
        s.MotivoRechazo.Should().Be("Falta la nota de crédito");
        s.ResueltaEn.Should().Be(Ahora);
        s.EstaViva.Should().BeFalse();
    }

    [Fact]
    public void Retirar_solo_mientras_esta_pendiente()
    {
        var s = Pendiente();
        s.Retirar(Ahora);
        s.Estado.Should().Be(EstadoSolicitudCancelacion.Retirada);

        var otra = Pendiente();
        otra.Autorizar(Contador, "Contador", Ahora);
        var act = () => otra.Retirar(Ahora);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CANCELACION_NO_PENDIENTE");
    }

    [Fact]
    public void Sin_autorizar_no_se_puede_enviar_ni_resolver_ante_el_SAT()
    {
        var s = Pendiente();

        ((Action)(() => s.MarcarEnProceso("EnProceso"))).Should().Throw<BusinessRuleException>().Where(e => e.Code == "CANCELACION_TRANSICION_INVALIDA");
        ((Action)(() => s.MarcarAceptada("Cancelado", Ahora))).Should().Throw<BusinessRuleException>().Where(e => e.Code == "CANCELACION_TRANSICION_INVALIDA");
    }

    [Fact]
    public void MarcarAceptada_resuelve_la_solicitud()
    {
        var s = Pendiente();
        s.Autorizar(Contador, "Contador", Ahora);
        s.MarcarEnProceso("EnProceso");
        s.MarcarAceptada("Cancelado", Ahora);

        s.Estado.Should().Be(EstadoSolicitudCancelacion.Aceptada);
        s.ResueltaEn.Should().Be(Ahora);
    }

    [Fact]
    public void Resolver_dos_veces_lanza_YA_RESUELTA()
    {
        var s = Pendiente();
        s.Autorizar(Contador, "Contador", Ahora);
        s.MarcarAceptada("Cancelado", Ahora);

        var act = () => s.MarcarRechazada("x", Ahora);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CANCELACION_YA_RESUELTA");
    }

    // ---- Comprobante FSM de cancelación ----

    private static FacturaVenta FacturaTimbrada()
    {
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(Guid.NewGuid(), "F-1", 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "03", "MXN", null, 2026, 5,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, 100m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado("11111111-1111-1111-1111-111111111111", null, null, null, Ahora, null, null);
        return fv;
    }

    [Fact]
    public void Comprobante_Timbrado_a_CancelacionPendiente_a_Cancelado()
    {
        var fv = FacturaTimbrada();

        fv.MarcarCancelacionPendiente();
        fv.Estado.Should().Be(EstadoTimbrado.CancelacionPendiente);

        fv.MarcarCancelado();
        fv.Estado.Should().Be(EstadoTimbrado.Cancelado);
    }

    [Fact]
    public void Comprobante_revierte_cancelacion_si_rechazada()
    {
        var fv = FacturaTimbrada();
        fv.MarcarCancelacionPendiente();

        fv.RevertirCancelacion();
        fv.Estado.Should().Be(EstadoTimbrado.Timbrado);
    }

    [Fact]
    public void Comprobante_no_cancelable_si_no_esta_Timbrado()
    {
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(Guid.NewGuid(), "F-1", 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "03", "MXN", null, 2026, 5,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);

        var act = () => fv.MarcarCancelacionPendiente();
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMPROBANTE_NO_CANCELABLE");
    }

    // ---- Efectos en agregados ----

    [Fact]
    public void PedidoFacturable_RevertirAFacturable_vuelve_a_Importado()
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(Guid.NewGuid(), "AW-1", Guid.NewGuid(), Guid.NewGuid(),
            "Cliente", (short)1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        pedido.MarcarFacturado(Guid.NewGuid());

        pedido.RevertirAFacturable();

        pedido.Estado.Should().Be(EstadoPedidoFacturable.Importado);
        pedido.ComprobanteVigenteId.Should().BeNull();
    }

    [Fact]
    public void Anticipo_Cancelar_pasa_a_Cancelado()
    {
        var a = Anticipo.Crear(Guid.NewGuid(), Guid.NewGuid(), "AAA010101AAA", TipoAnticipo.ClientesMxp, "MXN", 1000m, Guid.NewGuid());

        a.Cancelar();

        a.Estado.Should().Be(EstadoAnticipo.Cancelado);
    }
}
