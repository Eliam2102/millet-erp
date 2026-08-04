using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Pedidos.Queries;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Facturacion.UnitTests.Pedidos;

/// <summary>
/// FAC-UX-PR1 — el detalle del pedido enriquece con los masters para el
/// prellenado de la emisión: datos fiscales del cliente, claves SAT y tasas
/// por línea resueltas desde <c>compartido.producto_aw</c>.
/// </summary>
public sealed class PedidoDetallePrefillTests
{
    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private static async Task<PedidoFacturable> SembrarPedidoAsync(
        FacturacionDbContext db, Guid empresaId, Guid sucursalId, Guid? productoId,
        string? claveProdServ = null, string? claveUnidad = null)
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(empresaId, "AW-1", sucursalId, Guid.NewGuid(),
            "Cliente", (short)1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        pedido.AgregarLinea(productoId, "Producto", claveProdServ, claveUnidad, 2m, 100m, 0m, false);
        pedido.RecalcularTotal();
        db.PedidosFacturables.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }

    [Fact]
    public async Task Detalle_resuelve_claves_y_tasas_desde_el_master_cuando_la_linea_no_las_tiene()
    {
        var empresaId = Guid.NewGuid();
        var sucursalId = Guid.NewGuid();
        var productoId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        await SembrarPedidoAsync(db, empresaId, sucursalId, productoId);

        var master = new ProductoFiscalLectura(
            productoId, "Producto", "43211701", "MTK", "02", 0.16m, null, null, "Aw");
        var cliente = new ClienteFiscalLectura(
            Guid.NewGuid(), "AAA010101AAA", "Cliente SA", "601", "97000", "G03", "03", "PUE", "MXN", false);

        var detalle = await new PedidoFacturableDetalleHandler(
                db, new FakeClientesReadPort(cliente), new FakeProductosReadPort(master), new FakeCanalesVentaReadPort(), new FakeAlcanceCajaEvaluator())
            .Handle(new PedidoFacturableDetalleQuery(db.PedidosFacturables.Single().Id), CancellationToken.None);

        detalle.SucursalId.Should().Be(sucursalId);
        detalle.ClienteFiscal.Should().NotBeNull();
        detalle.ClienteFiscal!.Rfc.Should().Be("AAA010101AAA");
        detalle.ClienteFiscal.RegimenFiscal.Should().Be("601");
        detalle.ClienteFiscal.UsoCfdiDefault.Should().Be("G03");

        var linea = detalle.Lineas.Single();
        linea.ClaveProdServSat.Should().Be("43211701");
        linea.ClaveUnidadSat.Should().Be("MTK");
        linea.ObjetoImp.Should().Be("02");
        linea.TasaIvaTraslado.Should().Be(0.16m);
    }

    [Fact]
    public async Task Detalle_prefiere_las_claves_vivas_del_master_sobre_el_snapshot_de_la_linea()
    {
        // La ingesta congela la clave vigente del master al importar; si el
        // operador corrige el catálogo después (MTK→H87), el prefill debe
        // reflejar la clave viva — el snapshot solo es fallback.
        var empresaId = Guid.NewGuid();
        var productoId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        await SembrarPedidoAsync(db, empresaId, Guid.NewGuid(), productoId,
            claveProdServ: "01010101", claveUnidad: "MTK");

        var master = new ProductoFiscalLectura(
            productoId, "Producto", "43211701", "H87", "02", 0.16m, null, null, "Aw");

        var detalle = await new PedidoFacturableDetalleHandler(
                db, new FakeClientesReadPort(), new FakeProductosReadPort(master), new FakeCanalesVentaReadPort(), new FakeAlcanceCajaEvaluator())
            .Handle(new PedidoFacturableDetalleQuery(db.PedidosFacturables.Single().Id), CancellationToken.None);

        var linea = detalle.Lineas.Single();
        linea.ClaveProdServSat.Should().Be("43211701");
        linea.ClaveUnidadSat.Should().Be("H87");
        // Tasas/objetoImp no viven en la línea: siempre del master.
        linea.ObjetoImp.Should().Be("02");
    }

    [Fact]
    public async Task Detalle_cae_al_snapshot_cuando_el_master_no_tiene_claves()
    {
        var empresaId = Guid.NewGuid();
        var productoId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        await SembrarPedidoAsync(db, empresaId, Guid.NewGuid(), productoId,
            claveProdServ: "01010101", claveUnidad: "H87");

        // Master incompleto (claves aún no capturadas por el operador).
        var master = new ProductoFiscalLectura(
            productoId, "Producto", null, null, "02", 0.16m, null, null, "Aw");

        var detalle = await new PedidoFacturableDetalleHandler(
                db, new FakeClientesReadPort(), new FakeProductosReadPort(master), new FakeCanalesVentaReadPort(), new FakeAlcanceCajaEvaluator())
            .Handle(new PedidoFacturableDetalleQuery(db.PedidosFacturables.Single().Id), CancellationToken.None);

        var linea = detalle.Lineas.Single();
        linea.ClaveProdServSat.Should().Be("01010101");
        linea.ClaveUnidadSat.Should().Be("H87");
    }

    [Fact]
    public async Task Detalle_tolera_cliente_y_producto_fuera_del_master()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        await SembrarPedidoAsync(db, empresaId, Guid.NewGuid(), productoId: null);

        var detalle = await new PedidoFacturableDetalleHandler(
                db, new FakeClientesReadPort(), new FakeProductosReadPort(), new FakeCanalesVentaReadPort(), new FakeAlcanceCajaEvaluator())
            .Handle(new PedidoFacturableDetalleQuery(db.PedidosFacturables.Single().Id), CancellationToken.None);

        detalle.ClienteFiscal.Should().BeNull();
        var linea = detalle.Lineas.Single();
        linea.ClaveProdServSat.Should().BeNull();
        linea.ObjetoImp.Should().BeNull();
        linea.TasaIvaTraslado.Should().BeNull();
    }
}
