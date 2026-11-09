using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Pedidos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Xunit;

namespace Millet.Facturacion.UnitTests.Pedidos;

/// <summary>
/// M8: pedido facturado al 100 % por parcialidades seguía Importado. Importes del caso de la prueba manual; datos ficticios.
/// </summary>
public sealed class PedidoPorParcialidadesTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 9, 5, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();

    [Fact]
    public async Task M8_la_ultima_parcialidad_timbrada_marca_el_pedido_facturado()
    {
        await using var db = NuevaDb();
        var (pedido, primera, segunda) = await PedidoConDosSalidasAsync(db);

        var fa1 = await FacturaDeAsync(db, pedido, primera, timbrar: true);
        (await PedidoPorParcialidades.TomarPedidoSiCompletoAsync(db, fa1, pedido, default)).Should().BeFalse();
        pedido.Estado.Should().Be(EstadoPedidoFacturable.Importado);

        var fa2 = await FacturaDeAsync(db, pedido, segunda, timbrar: true);
        (await PedidoPorParcialidades.TomarPedidoSiCompletoAsync(db, fa2, pedido, default)).Should().BeTrue();
        pedido.Estado.Should().Be(EstadoPedidoFacturable.Facturado);
        pedido.ComprobanteVigenteId.Should().Be(fa2.Id);
    }

    [Fact]
    public async Task M8_sin_timbre_o_con_salidas_pendientes_el_pedido_sigue_importado()
    {
        await using var db = NuevaDb();
        var (pedido, primera, segunda) = await PedidoConDosSalidasAsync(db);

        await FacturaDeAsync(db, pedido, primera, timbrar: false);
        var fa2 = await FacturaDeAsync(db, pedido, segunda, timbrar: true);

        (await PedidoPorParcialidades.TomarPedidoSiCompletoAsync(db, fa2, pedido, default)).Should().BeFalse();
        pedido.Estado.Should().Be(EstadoPedidoFacturable.Importado);
    }

    [Fact]
    public async Task M8_salidas_que_no_cubren_todo_el_pedido_no_lo_marcan()
    {
        await using var db = NuevaDb();
        var pedido = PedidoDelCaso();
        db.PedidosFacturables.Add(pedido);
        var unica = SalidaFacturable.Registrar(pedido, [], OrigenSalidaFacturable.Manual, "SAL-1", [(1, 6m), (2, 6m)], Ahora);
        db.SalidasFacturables.Add(unica);
        await db.SaveChangesAsync();

        var fa = await FacturaDeAsync(db, pedido, unica, timbrar: true);

        (await PedidoPorParcialidades.TomarPedidoSiCompletoAsync(db, fa, pedido, default)).Should().BeFalse();
        pedido.Estado.Should().Be(EstadoPedidoFacturable.Importado);
    }

    private static PedidoFacturable PedidoDelCaso()
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(EmpresaId, "10425110", Guid.NewGuid(), Guid.NewGuid(), "Cliente Obra",
            (short)1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        pedido.AgregarLinea(Guid.NewGuid(), "Vidrio insulado A", "30171710", "H87", 10m, 545.87m, 0m, false);
        pedido.AgregarLinea(Guid.NewGuid(), "Vidrio insulado B", "30171710", "H87", 10m, 527.58m, 0m, false);
        pedido.RecalcularTotal();
        return pedido;
    }

    private static async Task<(PedidoFacturable, SalidaFacturable, SalidaFacturable)> PedidoConDosSalidasAsync(FacturacionDbContext db)
    {
        var pedido = PedidoDelCaso();
        db.PedidosFacturables.Add(pedido);
        var primera = SalidaFacturable.Registrar(pedido, [], OrigenSalidaFacturable.Manual, "SAL-1", [(1, 6m), (2, 6m)], Ahora);
        var segunda = SalidaFacturable.Registrar(pedido, [primera], OrigenSalidaFacturable.Manual, "SAL-2", [(1, 4m), (2, 4m)], Ahora);
        db.SalidasFacturables.AddRange(primera, segunda);
        await db.SaveChangesAsync();
        pedido.Total.Should().Be(10734.50m);
        primera.Total.Should().Be(6440.70m);
        segunda.Total.Should().Be(4293.80m);
        return (pedido, primera, segunda);
    }

    private static async Task<FacturaVenta> FacturaDeAsync(
        FacturacionDbContext db, PedidoFacturable pedido, SalidaFacturable salida, bool timbrar = false)
    {
        var factura = Borrador(pedido.Id);
        foreach (var ls in salida.Lineas.OrderBy(l => l.Posicion))
        {
            var lp = pedido.Lineas.Single(l => l.Posicion == ls.Posicion);
            var bruto = Math.Round(ls.Cantidad * lp.Precio, 2, MidpointRounding.AwayFromZero);
            factura.AgregarLinea(lp.ProductoId, "30171710", lp.ProductoDescripcion, "H87", ls.Cantidad, lp.Precio,
                bruto - ls.Importe, "02", 0.16m, null, null);
        }
        factura.RecalcularTotales();
        factura.LigarSalida(salida.Id, pedido.Id);
        if (timbrar)
        {
            factura.MarcarTimbradoEnProceso();
            factura.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        }
        db.FacturasVenta.Add(factura);
        await db.SaveChangesAsync();
        return factura;
    }

    private static FacturaVenta Borrador(Guid pedidoId) =>
        FacturaVenta.CrearBorrador(EmpresaId, "FA-000004", 4, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("XAXX010101000", "PUBLICO EN GENERAL", "616", "77524", "S01", "MEX", true),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "77524"), "PUE", "01", "MXN", null, 2026, 11,
            (short)1, ComportamientoFiscal.MostradorInmediato, pedidoId, null, null, false);

    private static FacturacionDbContext NuevaDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(EmpresaId));
}
