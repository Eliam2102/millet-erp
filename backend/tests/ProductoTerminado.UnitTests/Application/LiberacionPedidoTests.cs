using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Application.Liberacion;
using Millet.ProductoTerminado.Domain;

namespace Millet.ProductoTerminado.UnitTests.Application;

public sealed class LiberacionPedidoTests
{
    private static PedidoPt Pedido(EntornoPt t) => t.Db.PedidosPt.Single();
    private static IEnumerable<PedidoEstadoCambiadoEvent> Cambios(EntornoPt t) => t.Eventos.Eventos.OfType<PedidoEstadoCambiadoEvent>();

    private static async Task Escanear(EntornoPt t, int n)
    {
        for (var i = 1; i <= n; i++) await t.Escanear($"E{i}", pieza: i.ToString());
    }

    // P-096: con 8 de 10 el pedido no tiene estatus ni evento, aunque haya saldo.
    [Fact]
    public async Task Incompleto_no_se_retiene_ni_publica_evento()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 10);
        t.Saldos.Saldos["P1"] = 500m;
        await Escanear(t, 8);

        Pedido(t).Liberacion.Should().BeNull();
        Cambios(t).Should().BeEmpty();
    }

    // CA1: con el 100 % y saldo -> Retenido, un solo evento Retenido con cliente/moneda/total; reevaluar no repite.
    [Fact]
    public async Task Completo_con_saldo_queda_retenido_y_publica_un_solo_Retenido()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 10);
        t.Saldos.Saldos["P1"] = 500m;
        await Escanear(t, 10);
        (await t.Liberacion.ReevaluarAsync(Pedido(t), default)).Should().BeFalse();

        var p = Pedido(t);
        p.Liberacion.Should().Be(EstadoLiberacion.Retenido);
        p.MotivoLiberacion.Should().Be(MotivosPt.SaldoPendiente);
        var ev = Cambios(t).Should().ContainSingle().Subject;
        (ev.Estado, ev.Motivo).Should().Be(("Retenido", MotivosPt.SaldoPendiente));
    }

    // CA2: completo y saldo 0 -> 70 SaldoCubierto, un evento Liberado aunque se repita la evaluación.
    [Fact]
    public async Task Saldo_cero_libera_una_sola_vez_aunque_se_reevalue()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 10);
        t.Saldos.Saldos["P1"] = 500m;
        await Escanear(t, 10);
        t.Saldos.Saldos["P1"] = 0m;

        (await t.Liberacion.ReevaluarAsync(Pedido(t), default)).Should().BeTrue();
        (await t.Liberacion.ReevaluarAsync(Pedido(t), default)).Should().BeFalse();

        Pedido(t).MotivoLiberacion.Should().Be(MotivosPt.SaldoCubierto);
        Cambios(t).Count(e => e.Estado == "Liberado").Should().Be(1);
    }

    // Saldo 0 al completar: pasa directo a 70, sin evento de 69.
    [Fact]
    public async Task Completo_con_saldo_cero_se_libera_directo()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 3);
        t.Saldos.Saldos["P1"] = 0m;
        await Escanear(t, 3);

        Pedido(t).MotivoLiberacion.Should().Be(MotivosPt.SaldoCubierto);
        Cambios(t).Should().ContainSingle().Which.Estado.Should().Be("Liberado");
    }

    // P-096 / S15: REPARTO tampoco sale incompleto, aunque haya saldo.
    [Fact]
    public async Task Reparto_incompleto_no_se_libera()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 10, condicionPago: "REPARTO");
        t.Saldos.Saldos["P1"] = 500m;
        await Escanear(t, 3);

        Pedido(t).Liberacion.Should().BeNull();
        Cambios(t).Should().BeEmpty();
    }

    // REPARTO completo con saldo -> 70 Reparto, sin evento de 69.
    [Fact]
    public async Task Reparto_completo_se_libera_aunque_haya_saldo()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 10, condicionPago: "REPARTO");
        t.Saldos.Saldos["P1"] = 500m;
        await Escanear(t, 10);

        Pedido(t).Liberacion.Should().Be(EstadoLiberacion.Liberado);
        Pedido(t).MotivoLiberacion.Should().Be(MotivosPt.Reparto);
        Cambios(t).Should().ContainSingle().Which.Estado.Should().Be("Liberado");
    }

    // CA6 (Real): el adaptador provisional nunca cubre; el pedido completo sin saldo conocido no se libera por saldo ni en el barrido.
    [Fact]
    public async Task Sin_saldo_conocido_no_libera_por_saldo_ni_en_el_barrido()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 10);
        await Escanear(t, 10);

        var barrer = new BarrerRetenidosHandler(t.Db, t.Liberacion, NullLogger<BarrerRetenidosHandler>.Instance);
        (await barrer.Handle(new BarrerRetenidosCommand(), default)).Should().Be(0);
        Pedido(t).Liberacion.Should().Be(EstadoLiberacion.Retenido);
    }

    [Fact]
    public async Task Barrido_libera_los_retenidos_completos_con_saldo_cubierto_y_es_idempotente()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 2);
        t.Saldos.Saldos["P1"] = 500m;
        await Escanear(t, 2);
        t.Saldos.Saldos["P1"] = 0m;

        var barrer = new BarrerRetenidosHandler(t.Db, t.Liberacion, NullLogger<BarrerRetenidosHandler>.Instance);
        (await barrer.Handle(new BarrerRetenidosCommand(), default)).Should().Be(1);
        (await barrer.Handle(new BarrerRetenidosCommand(), default)).Should().Be(0);
        Cambios(t).Count(e => e.Estado == "Liberado").Should().Be(1);
    }

    // El barrido solo mira pedidos al 100 %: un Retenido incompleto (dato previo) no se libera aunque el saldo se cubra.
    [Fact]
    public async Task Barrido_ignora_retenidos_incompletos()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 3);
        t.Saldos.Saldos["P1"] = 500m;
        await Escanear(t, 3);
        t.Db.Movimientos.RemoveRange(t.Db.Movimientos);
        t.Db.Piezas.Remove(t.Db.Piezas.First());
        await t.Db.SaveChangesAsync();
        t.Saldos.Saldos["P1"] = 0m;

        var barrer = new BarrerRetenidosHandler(t.Db, t.Liberacion, NullLogger<BarrerRetenidosHandler>.Instance);
        (await barrer.Handle(new BarrerRetenidosCommand(), default)).Should().Be(0);
        Pedido(t).Liberacion.Should().Be(EstadoLiberacion.Retenido);
    }

    // CA4/CA5: la decisión de CxC libera un retenido; repetida no cambia nada; otros resultados se ignoran.
    [Fact]
    public async Task Decision_de_CxC_libera_una_vez_y_solo_Liberado_o_LiberadoConOverride()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("5000123", 1, 1);
        t.Saldos.Saldos["5000123"] = 500m;
        await t.Escanear("E1", pedido: "5000123"); // 1 de 1: completo
        var h = new AplicarDecisionCxcHandler(t.Db, t.Liberacion);

        (await h.Handle(new AplicarDecisionCxcCommand(EntornoPt.Empresa, "5000123", "Retenido"), default)).Should().BeFalse();
        (await h.Handle(new AplicarDecisionCxcCommand(EntornoPt.Empresa, "5000123", "PendienteAutorizacion"), default)).Should().BeFalse();
        Pedido(t).Liberacion.Should().Be(EstadoLiberacion.Retenido);

        (await h.Handle(new AplicarDecisionCxcCommand(EntornoPt.Empresa, "5000123", "Liberado"), default)).Should().BeTrue();
        (await h.Handle(new AplicarDecisionCxcCommand(EntornoPt.Empresa, "5000123", "Liberado"), default)).Should().BeFalse();
        Pedido(t).MotivoLiberacion.Should().Be(MotivosPt.DecisionCxc);
        Cambios(t).Count(e => e.Estado == "Liberado").Should().Be(1);
    }

    [Fact]
    public async Task Decision_de_pedido_desconocido_se_ignora()
    {
        using var t = new EntornoPt();
        var h = new AplicarDecisionCxcHandler(t.Db, t.Liberacion);
        (await h.Handle(new AplicarDecisionCxcCommand(EntornoPt.Empresa, "NOEXISTE", "Liberado"), default)).Should().BeFalse();
    }
}
