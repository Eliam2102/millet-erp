using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Pedidos.Queries;
using Millet.Facturacion.Application.Pedidos.RegistrarSalidaFacturable;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;
using Xunit;

namespace Millet.Facturacion.UnitTests.Pedidos;

/// <summary>FAC-08: salidas parciales facturables de un pedido que no se modifica. Datos ficticios.</summary>
public sealed class SalidaFacturableTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 9, 16, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();

    private static PedidoFacturable Pedido(decimal cantidad = 100m, decimal precio = 10m, decimal descuento = 0m)
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(EmpresaId, "10425068", Guid.NewGuid(), Guid.NewGuid(), "Cliente Obra",
            (short)1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        pedido.AgregarLinea(Guid.NewGuid(), "Vidrio templado 6 mm", "30171500", "H87", cantidad, precio, descuento, false);
        pedido.AgregarLinea(Guid.NewGuid(), "Canto pulido", "30171500", "H87", 10m, 5m, 0m, false);
        pedido.RecalcularTotal();
        return pedido;
    }

    [Fact]
    public void Primera_salida_de_60_y_segunda_de_40_dejan_el_pedido_en_100()
    {
        var pedido = Pedido();

        var primera = SalidaFacturable.Registrar(pedido, [], OrigenSalidaFacturable.ProductoTerminado, "SAL-1", [(1, 60m)], Ahora);
        var segunda = SalidaFacturable.Registrar(pedido, [primera], OrigenSalidaFacturable.ProductoTerminado, "SAL-2", [(1, 40m)], Ahora);

        primera.NumeroParcialidad.Should().Be(1);
        primera.Total.Should().Be(600m);
        segunda.NumeroParcialidad.Should().Be(2);
        segunda.Total.Should().Be(400m);
        pedido.Lineas.Single(l => l.Posicion == 1).Cantidad.Should().Be(100m);
        pedido.Total.Should().Be(1050m);
        pedido.Estado.Should().Be(EstadoPedidoFacturable.Importado);
    }

    [Fact]
    public void Una_salida_que_rebasa_lo_pendiente_se_rechaza()
    {
        var pedido = Pedido();
        var primera = SalidaFacturable.Registrar(pedido, [], OrigenSalidaFacturable.ProductoTerminado, "SAL-1", [(1, 60m)], Ahora);

        var exceso = () => SalidaFacturable.Registrar(pedido, [primera], OrigenSalidaFacturable.ProductoTerminado, "SAL-2", [(1, 41m)], Ahora);

        exceso.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("SALIDA_EXCEDE_PEDIDO");
    }

    [Fact]
    public void El_descuento_se_reparte_y_la_ultima_parcialidad_cuadra_con_el_pedido()
    {
        var pedido = Pedido(cantidad: 3m, precio: 10m, descuento: 1m);
        var importePedido = pedido.Lineas.Single(l => l.Posicion == 1).Importe;

        var a = SalidaFacturable.Registrar(pedido, [], OrigenSalidaFacturable.ContenedorExportacion, "CONT-1", [(1, 1m)], Ahora);
        var b = SalidaFacturable.Registrar(pedido, [a], OrigenSalidaFacturable.ContenedorExportacion, "CONT-2", [(1, 1m)], Ahora);
        var c = SalidaFacturable.Registrar(pedido, [a, b], OrigenSalidaFacturable.ContenedorExportacion, "CONT-3", [(1, 1m)], Ahora);

        a.Total.Should().Be(9.67m);
        b.Total.Should().Be(9.67m);
        (a.Total + b.Total + c.Total).Should().Be(importePedido);
    }

    [Theory]
    [InlineData(3, "SALIDA_POSICION_INEXISTENTE")]
    [InlineData(0, "SALIDA_POSICION_INEXISTENTE")]
    public void La_posicion_debe_existir_en_el_pedido(int posicion, string codigo)
    {
        var accion = () => SalidaFacturable.Registrar(Pedido(), [], OrigenSalidaFacturable.Manual, "SAL-1", [(posicion, 1m)], Ahora);

        accion.Should().Throw<BusinessRuleException>().Which.Code.Should().Be(codigo);
    }

    [Fact]
    public void Un_pedido_facturado_completo_no_admite_salidas()
    {
        var pedido = Pedido();
        pedido.MarcarFacturado(Guid.NewGuid());

        var accion = () => SalidaFacturable.Registrar(pedido, [], OrigenSalidaFacturable.Manual, "SAL-1", [(1, 1m)], Ahora);

        accion.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("SALIDA_PEDIDO_NO_FACTURABLE");
    }

    [Fact]
    public async Task Reenviar_la_misma_salida_no_crea_otra_ni_cambia_importes()
    {
        await using var db = NuevaDb();
        var pedido = Pedido();
        db.PedidosFacturables.Add(pedido);
        await db.SaveChangesAsync();
        var handler = new RegistrarSalidaFacturableHandler(db, new FakeClock(Ahora));
        var comando = new RegistrarSalidaFacturableCommand(null, "10425068", OrigenSalidaFacturable.ProductoTerminado, "SAL-1",
            [new SalidaFacturableLineaDto(1, 60m)]);

        var primera = await handler.Handle(comando, CancellationToken.None);
        var reenvio = await handler.Handle(comando, CancellationToken.None);

        primera.YaRegistrada.Should().BeFalse();
        reenvio.YaRegistrada.Should().BeTrue();
        reenvio.Id.Should().Be(primera.Id);
        reenvio.Total.Should().Be(600m);
        (await db.SalidasFacturables.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Reenviar_la_referencia_con_otras_cantidades_se_rechaza_sin_cambiar_la_salida()
    {
        await using var db = NuevaDb();
        var pedido = Pedido();
        db.PedidosFacturables.Add(pedido);
        await db.SaveChangesAsync();
        var handler = new RegistrarSalidaFacturableHandler(db, new FakeClock(Ahora));
        await handler.Handle(new RegistrarSalidaFacturableCommand(pedido.Id, null, OrigenSalidaFacturable.Manual, "SAL-1",
            [new SalidaFacturableLineaDto(1, 60m)]), CancellationToken.None);

        var accion = () => handler.Handle(new RegistrarSalidaFacturableCommand(pedido.Id, null, OrigenSalidaFacturable.Manual, "SAL-1",
            [new SalidaFacturableLineaDto(1, 70m)]), CancellationToken.None);

        (await accion.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("SALIDA_REFERENCIA_DISTINTA");
        (await db.SalidasFacturables.SingleAsync()).Total.Should().Be(600m);
    }

    [Fact]
    public async Task El_avance_del_pedido_se_calcula_con_sus_salidas()
    {
        await using var db = NuevaDb();
        var pedido = Pedido();
        db.PedidosFacturables.Add(pedido);
        await db.SaveChangesAsync();
        var handler = new RegistrarSalidaFacturableHandler(db, new FakeClock(Ahora));
        await handler.Handle(new RegistrarSalidaFacturableCommand(pedido.Id, null, OrigenSalidaFacturable.Manual, "SAL-1",
            [new SalidaFacturableLineaDto(1, 60m), new SalidaFacturableLineaDto(2, 10m)]), CancellationToken.None);

        var r = await new PedidoSalidasHandler(db).Handle(new PedidoSalidasQuery(pedido.Id), CancellationToken.None);

        r.Salidas.Should().ContainSingle().Which.FacturaId.Should().BeNull();
        r.Avance.Select(a => (a.Posicion, a.Pedida, a.EnSalidas, a.Facturada))
            .Should().Equal((1, 100m, 60m, 0m), (2, 10m, 10m, 0m));
        r.TotalFacturado.Should().Be(0m);
    }

    private static FacturacionDbContext NuevaDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(EmpresaId));
}
