using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Pedidos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;
using Xunit;

namespace Millet.Facturacion.UnitTests.Pedidos;

/// <summary>
/// M7: centavo de más al facturar un pedido por parcialidades. Importes del caso de la prueba manual; datos ficticios.
/// </summary>
public sealed class IvaParcialidadesTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 9, 5, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();

    [Fact]
    public void M7_iva_redondeado_sobre_el_acumulado_suma_el_iva_de_la_posicion()
    {
        // Posición 1: 10 × 545.87 = 5,458.70 → IVA 873.39. Por separado 524.04 + 349.36 = 873.40.
        var primera = IvaParcialidades.IvaAcumulado(0m, 3275.22m, 0.16m);
        var segunda = IvaParcialidades.IvaAcumulado(3275.22m, 2183.48m, 0.16m);

        primera.Should().Be(524.04m);
        segunda.Should().Be(349.35m);
        (primera + segunda).Should().Be(873.39m);
        IvaParcialidades.IvaAcumulado(3165.48m, 2110.32m, 0.16m).Should().Be(337.65m);
    }

    [Fact]
    public async Task M7_las_facturas_de_las_parcialidades_suman_el_total_del_pedido()
    {
        await using var db = NuevaDb();
        var (pedido, primera, segunda) = await PedidoConDosSalidasAsync(db);

        var fa1 = await FacturaDeAsync(db, pedido, primera);
        var fa2 = await FacturaDeAsync(db, pedido, segunda);

        fa1.Total.Should().Be(7471.22m);
        fa2.Total.Should().Be(4980.80m);
        (fa1.Total + fa2.Total).Should().Be(12452.02m);
        (fa1.ImpuestosTrasladados + fa2.ImpuestosTrasladados).Should().Be(1717.52m);
    }

    [Fact]
    public async Task M7_el_orden_de_facturacion_no_cambia_el_iva_de_cada_parcialidad()
    {
        await using var db = NuevaDb();
        var (pedido, primera, segunda) = await PedidoConDosSalidasAsync(db);

        var fa2 = await FacturaDeAsync(db, pedido, segunda);
        var fa1 = await FacturaDeAsync(db, pedido, primera);

        fa1.Total.Should().Be(7471.22m);
        fa2.Total.Should().Be(4980.80m);
    }

    [Fact]
    public void M7_el_ajuste_de_iva_no_puede_alejarse_un_centavo_de_la_base()
    {
        var factura = Borrador(Guid.NewGuid());
        factura.AgregarLinea(null, "30171710", "Vidrio", "H87", 4m, 545.87m, 0m, "02", 0.16m, null, null);
        factura.RecalcularTotales();

        factura.AjustarIvaParcialidad(1, 349.35m);
        factura.ImpuestosTrasladados.Should().Be(349.35m);

        var lejos = () => factura.AjustarIvaParcialidad(1, 349.34m);
        lejos.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("LINEA_IVA_AJUSTE_INVALIDO");
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
        await IvaParcialidades.AjustarIvaAsync(db, factura, salida, default);
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
