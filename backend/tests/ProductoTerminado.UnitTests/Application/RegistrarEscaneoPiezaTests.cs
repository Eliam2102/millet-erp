using Microsoft.EntityFrameworkCore;
using Millet.ProductoTerminado.Domain;

namespace Millet.ProductoTerminado.UnitTests.Application;

public sealed class RegistrarEscaneoPiezaTests
{
    [Fact]
    public async Task Dos_piezas_generan_dos_altas_dos_movimientos_y_dos_eventos()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 4);
        var a = await t.Escanear("E1", pieza: "1");
        var b = await t.Escanear("E2", pieza: "2");
        a.Resultado.Should().Be("Alta");
        b.Resultado.Should().Be("Alta");
        (await t.Db.Piezas.CountAsync()).Should().Be(2);
        (await t.Db.Movimientos.CountAsync()).Should().Be(2);
        t.Eventos.Eventos.Should().HaveCount(2);
    }

    [Fact]
    public async Task Misma_fila_dos_veces_es_idempotente()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 4);
        var primera = await t.Escanear("E1");
        var segunda = await t.Escanear("E1");
        primera.Repetido.Should().BeFalse();
        segunda.Repetido.Should().BeTrue();
        segunda.EscaneoId.Should().Be(primera.EscaneoId);
        (await t.Db.Piezas.CountAsync()).Should().Be(1);
        (await t.Db.Movimientos.CountAsync()).Should().Be(1);
        t.Eventos.Eventos.Should().HaveCount(1);
    }

    [Fact]
    public async Task Pedido_desconocido_queda_pospuesto_sin_pieza()
    {
        using var t = new EntornoPt();
        var r = await t.Escanear("E1", pedido: "NOEXISTE");
        r.Resultado.Should().Be("Pospuesto");
        r.Motivo.Should().Be(MotivosPt.PedidoInexistente);
        (await t.Db.Piezas.CountAsync()).Should().Be(0);
        (await t.Db.Escaneos.SingleAsync()).Intentos.Should().Be(1);
    }

    [Fact]
    public async Task Pieza_mayor_a_las_esperadas_es_excepcion()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 4);
        var r = await t.Escanear("E5", pieza: "5");
        r.Resultado.Should().Be("Excepcion");
        r.Motivo.Should().Be(MotivosPt.PiezaExcedePosicion);
        (await t.Db.Piezas.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Numero_de_pieza_vacio_es_identidad_incompleta()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 4);
        var r = await t.Escanear("E1", pieza: " ");
        r.Resultado.Should().Be("Excepcion");
        r.Motivo.Should().Be(MotivosPt.IdentidadIncompleta);
    }

    [Fact]
    public async Task Clave_de_sucursal_no_resuelta_es_excepcion()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 4);
        var r = await t.Escanear("E1", clave: "99");
        r.Resultado.Should().Be("Excepcion");
        r.Motivo.Should().Be(MotivosPt.SucursalSinClaveAw);
    }

    [Fact]
    public async Task Etiqueta_con_espacios_equivale_a_la_recortada()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 4);
        await t.Escanear("E1");
        var r = await t.Escanear("  E1 ");
        r.Repetido.Should().BeTrue();
        (await t.Db.Piezas.CountAsync()).Should().Be(1);
    }
}
