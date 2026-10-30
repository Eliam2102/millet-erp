using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.ProductoTerminado.Application.Escaneos;
using Millet.ProductoTerminado.Domain;
using Millet.SharedKernel.Application;

namespace Millet.ProductoTerminado.UnitTests.Application;

public sealed class PospuestosTests
{
    private sealed class RelojMutable(DateTimeOffset ahora) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = ahora;
    }

    // EntornoPt registra con el reloj fijo de las 12:00 UTC (RecibidoAt); el reintento usa un reloj movible.
    private static readonly DateTimeOffset Inicio = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private static (ReintentarPospuestosHandler Handler, RelojMutable Reloj) Reintento(EntornoPt t)
    {
        var reloj = new RelojMutable(Inicio);
        return (new ReintentarPospuestosHandler(t.Db, new EscaneoProcesador(t.Db, t.Pedidos, t.Sucursales, t.Eventos, reloj), reloj, NullLogger<ReintentarPospuestosHandler>.Instance), reloj);
    }

    private static ReintentarPospuestosCommand Cmd(int max = 5) => new(EntornoPt.Empresa, 24, max, 100);

    [Fact]
    public async Task Pedido_que_aparece_crea_la_pieza_y_la_fila_queda_en_alta()
    {
        using var t = new EntornoPt();
        await t.Escanear("E1", pedido: "P1");
        t.AgregarPedido("P1", 1, 2);
        var (h, _) = Reintento(t);
        (await h.Handle(Cmd(), CancellationToken.None)).Should().Be(1);
        (await t.Db.Piezas.CountAsync()).Should().Be(1);
        (await t.Db.Escaneos.SingleAsync()).Resultado.Should().Be(ResultadoEscaneo.Alta);
    }

    [Fact]
    public async Task A_la_hora_sigue_pospuesto_y_los_intentos_no_superan_el_tope()
    {
        using var t = new EntornoPt();
        await t.Escanear("E1", pedido: "P1");
        var (h, reloj) = Reintento(t);
        reloj.UtcNow = Inicio.AddHours(1);
        for (var i = 0; i < 8; i++) (await h.Handle(Cmd(3), CancellationToken.None)).Should().Be(0);
        var e = await t.Db.Escaneos.SingleAsync();
        e.Resultado.Should().Be(ResultadoEscaneo.Pospuesto);
        e.Intentos.Should().Be(3);
    }

    [Fact]
    public async Task Pasadas_24_horas_se_promueve_a_excepcion_con_el_mismo_motivo()
    {
        using var t = new EntornoPt();
        await t.Escanear("E1", pedido: "P1");
        var (h, reloj) = Reintento(t);
        reloj.UtcNow = Inicio.AddHours(24).AddMinutes(1);
        (await h.Handle(Cmd(), CancellationToken.None)).Should().Be(1);
        var e = await t.Db.Escaneos.SingleAsync();
        e.Resultado.Should().Be(ResultadoEscaneo.Excepcion);
        e.Motivo.Should().Be(MotivosPt.PedidoInexistente);
    }

    [Fact]
    public async Task Marca_de_agua_es_el_maximo_del_origen_pedido_y_null_sin_filas()
    {
        using var t = new EntornoPt();
        var q = new MarcaAguaHandler(t.Db);
        (await q.Handle(new ObtenerMarcaAguaEscaneosQuery(EntornoPt.Empresa, "Demo"), CancellationToken.None)).Should().BeNull();
        var tarde = new DateTime(2026, 10, 28, 9, 30, 0, DateTimeKind.Unspecified);
        await t.Escanear("E1", at: new DateTime(2026, 10, 28, 8, 0, 0, DateTimeKind.Unspecified));
        await t.Escanear("E2", at: tarde);
        t.Db.Escaneos.Add(EscaneoPt.Registrar(EntornoPt.Empresa, "Real", "R1", "P1", 1, null, "1", 2821, null,
            tarde.AddHours(5), tarde.AddHours(5), "01", null, Inicio));
        await t.Db.SaveChangesAsync();
        (await q.Handle(new ObtenerMarcaAguaEscaneosQuery(EntornoPt.Empresa, "Demo"), CancellationToken.None)).Should().Be(tarde);
    }
}
