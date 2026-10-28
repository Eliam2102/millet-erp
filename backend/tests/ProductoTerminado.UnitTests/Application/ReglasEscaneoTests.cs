using Microsoft.EntityFrameworkCore;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Domain;

namespace Millet.ProductoTerminado.UnitTests.Application;

public sealed class ReglasEscaneoTests
{
    private static DateTime Hora(int minuto) => new(2026, 10, 28, 8, minuto, 0, DateTimeKind.Unspecified);

    [Fact]
    public async Task Etiqueta_conocida_con_otra_hora_es_reposicion_sin_pieza_ni_evento()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 3);
        await t.Escanear("E1", at: Hora(0));
        var r = await t.Escanear("E1", at: Hora(5));
        r.Resultado.Should().Be("Reposicion");
        (await t.Db.Piezas.CountAsync()).Should().Be(1);
        (await t.Db.Movimientos.CountAsync()).Should().Be(1);
        t.Eventos.Eventos.Should().HaveCount(1);
    }

    [Fact]
    public async Task Reposiciones_sobre_posicion_terminada_no_suben_el_avance()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 3);
        for (var i = 1; i <= 3; i++) await t.Escanear($"E{i}", pieza: i.ToString(), at: Hora(i));
        for (var i = 1; i <= 3; i++)
            (await t.Escanear($"E{i}", pieza: i.ToString(), at: Hora(10 + i))).Resultado.Should().Be("Reposicion");
        (await t.Db.Piezas.CountAsync()).Should().Be(3);
        (await t.Db.Escaneos.CountAsync(x => x.Resultado == ResultadoEscaneo.Reposicion)).Should().Be(3);
    }

    [Fact]
    public async Task Misma_reposicion_dos_veces_es_una_fila()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 3);
        await t.Escanear("E1", at: Hora(0));
        await t.Escanear("E1", at: Hora(5));
        var r = await t.Escanear("E1", at: Hora(5));
        r.Repetido.Should().BeTrue();
        (await t.Db.Escaneos.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Pedido_cancelado_es_excepcion()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 3, estado: "Cancelado");
        var r = await t.Escanear("E1");
        r.Motivo.Should().Be(MotivosPt.PedidoCancelado);
        (await t.Db.Piezas.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Cantidad_no_entera_es_excepcion()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 2.5m);
        (await t.Escanear("E1")).Motivo.Should().Be(MotivosPt.CantidadNoEntera);
    }

    [Fact]
    public async Task Linea_E48_no_espera_piezas()
    {
        using var t = new EntornoPt();
        t.Pedidos.Pedidos["P1"] = new PedidoPtOrigen(Guid.NewGuid(), "P1", EntornoPt.Sucursal, "Abierto", null, 1,
            [new LineaPtOrigen(1, null, "Servicio", 5, "E48")]);
        (await t.Escanear("E1")).Motivo.Should().Be(MotivosPt.PiezaExcedePosicion);
    }

    [Fact]
    public async Task Posicion_inexistente_es_excepcion()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 3);
        (await t.Escanear("E1", pos: 9)).Motivo.Should().Be(MotivosPt.PosicionInexistente);
    }

    [Fact]
    public async Task Nueva_version_del_origen_refresca_posiciones()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 3);
        await t.Escanear("E1", pieza: "1");
        var o = t.Pedidos.Pedidos["P1"];
        t.Pedidos.Pedidos["P1"] = o with { VersionOrigen = 2, Lineas = [new LineaPtOrigen(1, null, "Vidrio", 4, null)] };
        (await t.Escanear("E4", pieza: "4")).Resultado.Should().Be("Alta");
    }

    [Fact]
    public async Task Piezas_fuera_de_orden_entran_todas()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 3);
        foreach (var n in new[] { 3, 1, 2 })
            (await t.Escanear($"E{n}", pieza: n.ToString())).Resultado.Should().Be("Alta");
        (await t.Db.Piezas.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Misma_ranura_con_otra_etiqueta_es_duplicada()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 3);
        await t.Escanear("E1", pieza: "1");
        var r = await t.Escanear("OTRA", pieza: "1");
        r.Motivo.Should().Be(MotivosPt.PiezaDuplicada);
        (await t.Db.Piezas.CountAsync()).Should().Be(1);
    }
}
