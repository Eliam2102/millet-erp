using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Pedidos;

public sealed class PedidoFacturableAggregateTests
{
    private static PedidoFacturable Manual() => PedidoFacturable.CrearManual(
        empresaId: Guid.NewGuid(),
        numeroPedido: "MAN-1",
        sucursalId: Guid.NewGuid(),
        clienteId: Guid.NewGuid(),
        clienteNombre: "Cliente de prueba",
        canalVentaId: (short)10,
        comportamientoFiscal: ComportamientoFiscal.Administrativa,
        moneda: "MXN",
        obraId: null,
        obraNombre: null,
        comentarios: "no va al XML",
        capturadoPor: Guid.NewGuid());

    [Fact]
    public void CrearManual_arranca_Importado_origen_Manual()
    {
        var p = Manual();

        p.Estado.Should().Be(EstadoPedidoFacturable.Importado);
        p.Origen.Should().Be(OrigenPedido.Manual);
        p.CapturadoPor.Should().NotBeNull();
    }

    [Fact]
    public void AgregarLinea_y_RecalcularTotal_suma_importes()
    {
        var p = Manual();
        p.AgregarLinea(null, "Producto A", "01010101", "H87", 3m, 50m, 0m, false);
        p.AgregarLinea(null, "Producto B", "01010101", "H87", 1m, 100m, 10m, false);

        p.RecalcularTotal();

        p.Lineas.Should().HaveCount(2);
        p.Total.Should().Be(240m); // 150 + 90
    }

    [Fact]
    public void EditarCabecera_actualiza_y_LimpiarLineas_reemplaza()
    {
        var p = Manual();
        p.AgregarLinea(null, "Vieja", "01010101", "H87", 1m, 10m, 0m, false);

        p.EditarCabecera(Guid.NewGuid(), "Nuevo cliente", (short)1,
            ComportamientoFiscal.MostradorInmediato, "USD", 99L, "Obra X", "nuevo comentario");
        p.LimpiarLineas();
        p.AgregarLinea(null, "Nueva", "01010101", "H87", 2m, 25m, 0m, false);
        p.RecalcularTotal();

        p.ClienteNombre.Should().Be("Nuevo cliente");
        p.Moneda.Should().Be("USD");
        p.ObraId.Should().Be(99L);
        p.Lineas.Should().ContainSingle();
        p.Total.Should().Be(50m);
    }

    [Fact]
    public void AgregarLinea_tras_Facturado_lanza_no_editable()
    {
        var p = Manual();
        p.MarcarFacturado(Guid.NewGuid());

        var act = () => p.AgregarLinea(null, "X", "01010101", "H87", 1m, 1m, 0m, false);

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PEDIDO_NO_EDITABLE");
    }

    [Fact]
    public void MarcarFacturado_liga_comprobante_vigente()
    {
        var p = Manual();
        var comprobanteId = Guid.NewGuid();

        p.MarcarFacturado(comprobanteId);

        p.Estado.Should().Be(EstadoPedidoFacturable.Facturado);
        p.ComprobanteVigenteId.Should().Be(comprobanteId);
    }

    [Fact]
    public void AgregarLinea_descuento_excede_lanza()
    {
        var p = Manual();

        var act = () => p.AgregarLinea(null, "X", "01010101", "H87", 1m, 10m, 20m, false);

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PEDIDO_LINEA_DESCUENTO_EXCEDE");
    }
}
