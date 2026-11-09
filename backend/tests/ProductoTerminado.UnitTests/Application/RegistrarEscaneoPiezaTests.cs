using Microsoft.EntityFrameworkCore;
using Millet.ProductoTerminado.Application;
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
        t.Eventos.Eventos.OfType<PiezaIngresadaEvent>().Should().HaveCount(2);
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
        t.Eventos.Eventos.OfType<PiezaIngresadaEvent>().Should().HaveCount(1);
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
    public async Task Pieza_cero_de_aw_asigna_ranura_correlativa_en_posicion()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 2);
        var a = await t.Escanear("E1", pieza: "0");
        var b = await t.Escanear("E2", pieza: "0");
        a.Resultado.Should().Be("Alta");
        b.Resultado.Should().Be("Alta");
        var piezas = await t.Db.Piezas.OrderBy(x => x.NumeroPieza).ToListAsync();
        piezas.Should().HaveCount(2);
        piezas[0].NumeroPieza.Should().Be(1);
        piezas[0].Etiqueta.Should().Be("E1");
        piezas[1].NumeroPieza.Should().Be(2);
        piezas[1].Etiqueta.Should().Be("E2");
    }

    [Fact]
    public async Task Pieza_cero_de_aw_excediendo_piezas_esperadas_marca_excepcion()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 1);
        var a = await t.Escanear("E1", pieza: "0");
        var b = await t.Escanear("E2", pieza: "0");
        a.Resultado.Should().Be("Alta");
        b.Resultado.Should().Be("Excepcion");
        b.Motivo.Should().Be(MotivosPt.PiezaExcedePosicion);
        (await t.Db.Piezas.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Reescaneo_de_etiqueta_con_pieza_cero_marca_reposicion_sin_consumir_ranuras()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 2);
        var a1 = await t.Escanear("E1", pieza: "0");
        var a2 = await t.Escanear("E1", pieza: "0", at: new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Unspecified));
        var b1 = await t.Escanear("E2", pieza: "0");
        a1.Resultado.Should().Be("Alta");
        a2.Resultado.Should().Be("Reposicion");
        b1.Resultado.Should().Be("Alta");
        var piezas = await t.Db.Piezas.OrderBy(x => x.NumeroPieza).ToListAsync();
        piezas.Should().HaveCount(2);
        piezas[0].NumeroPieza.Should().Be(1);
        piezas[1].NumeroPieza.Should().Be(2);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("-1")]
    [InlineData("abc")]
    public async Task Numero_de_pieza_invalido_es_identidad_incompleta(string piezaInvalida)
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 4);
        var r = await t.Escanear("E1", pieza: piezaInvalida);
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
    public async Task Sin_clave_de_sucursal_toma_la_sucursal_del_pedido()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 4);
        var r = await t.Escanear("E1", clave: null);
        r.Resultado.Should().Be("Alta");
        (await t.Db.Piezas.SingleAsync()).SucursalId.Should().Be(EntornoPt.Sucursal);
    }

    [Fact]
    public async Task Sin_clave_y_sin_pedido_queda_pospuesto()
    {
        using var t = new EntornoPt();
        var r = await t.Escanear("E1", clave: null);
        r.Resultado.Should().Be("Pospuesto");
        r.Motivo.Should().Be(MotivosPt.PedidoInexistente);
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
