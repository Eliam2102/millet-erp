using System.Text.Json;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;

namespace Millet.Facturacion.UnitTests.Integration;

/// <summary>
/// U1.6: bloque contable de los eventos timbrados (mapper único) y
/// compatibilidad con eventos antiguos. Datos DEMO.
/// </summary>
public sealed class EventosContablesFacturacionTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Sucursal = Guid.NewGuid();
    private static readonly DateTimeOffset Timbrado = new(2026, 10, 1, 3, 30, 0, TimeSpan.Zero); // 30-sep 21:30 en Mérida

    private static DatosFiscalesReceptor Receptor() => new("DEM010101AB1", "DEMO VIDRIOS", "601", "97000", "G03", "MEX", false);
    private static DatosFiscalesEmisor Emisor() => new("MIL010101AAA", "Millet", "601", "97000");

    private static FacturaVenta Factura(string moneda = "MXN", decimal? tc = null,
        params (Guid? ProductoId, decimal Valor, decimal? RetIva, decimal? RetIsr)[] lineas)
    {
        var fv = FacturaVenta.CrearBorrador(Empresa, "F-DEMO-1", 1, Sucursal, null, null, Receptor(), Emisor(),
            "PPD", "99", moneda, tc, 2026, 9, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        foreach (var l in lineas)
            fv.AgregarLinea(l.ProductoId, "30171500", "Vidrio DEMO", "H87", 1m, l.Valor, 0m, "02", 0.16m, l.RetIva, l.RetIsr);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Timbrado, null, null);
        return fv;
    }

    [Fact]
    public void U1_6a_factura_11600_con_ranura_1160_trae_desglose_y_la_NC_el_suyo()
    {
        var templado = Guid.NewGuid();
        var insulado = Guid.NewGuid();
        var cliente = Guid.NewGuid();
        var factura = Factura("USD", 18.25m, (templado, 6000m, null, null), (insulado, 4000m, null, null));
        var tipos = new Dictionary<Guid, string?> { [templado] = "TEMPLADO", [insulado] = "INSULADO" };

        var evt = EventosContablesFacturacion.FacturaVentaTimbrada(factura, Timbrado, cliente, tipos);

        evt.Total.Should().Be(11600m);
        evt.Subtotal.Should().Be(10000m);
        evt.Iva.Should().Be(1600m);
        evt.Descuento.Should().Be(0m);
        evt.SucursalId.Should().Be(Sucursal);
        evt.ClienteId.Should().Be(cliente);
        evt.TipoCambio.Should().Be(18.25m);
        evt.FechaContable.Should().Be(new DateOnly(2026, 9, 30), "se usa el día local de Millet, no el UTC");
        evt.Retenciones.Should().BeNull();
        evt.Lineas!.Select(l => (l.TipoProducto, l.Importe, l.Iva)).Should().BeEquivalentTo(
            [("TEMPLADO", 6000m, 960m), ("INSULADO", 4000m, 640m)]);

        var ranura = NotaCredito.CrearRanura(Empresa, "NC-R", 1, Sucursal, null, null, Receptor(), Emisor(),
            "99", "USD", 18.25m, 2026, 9, 1, factura.Id, 1160m, 0.16m, "Ranura pedido DEMO");
        ranura.MarcarTimbradoEnProceso();
        ranura.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Timbrado, null, null);

        var nc = EventosContablesFacturacion.NotaCreditoTimbrada(ranura, Timbrado, factura.Id);

        nc.Motivo.Should().Be(MotivoNotaCredito.Ranura.ToString());
        nc.Total.Should().Be(1160m);
        nc.Subtotal.Should().Be(1000m);
        nc.Iva.Should().Be(160m);
        nc.FacturaRelacionadaId.Should().Be(factura.Id);
        nc.SucursalId.Should().Be(Sucursal);
        nc.Moneda.Should().Be("USD");
        nc.TipoCambio.Should().Be(18.25m);
        nc.FechaContable.Should().Be(new DateOnly(2026, 9, 30));
    }

    [Fact]
    public void Retenciones_se_desglosan_por_impuesto_y_cuadran_con_el_total()
    {
        // Servicio DEMO a persona moral: retención IVA 2/3 (10.6667 %) e ISR 10 %.
        var factura = Factura(lineas: (null, 1000m, 0.106667m, 0.10m));

        var evt = EventosContablesFacturacion.FacturaVentaTimbrada(factura, Timbrado);

        evt.RetencionesTotal.Should().Be(factura.Retenciones);
        evt.Retenciones!.Sum(r => r.Importe).Should().Be(factura.Retenciones);
        evt.Retenciones.Should().Contain(r => r.Impuesto == EventosContablesFacturacion.ImpuestoIva && r.Importe == 106.67m);
        evt.Retenciones.Should().Contain(r => r.Impuesto == EventosContablesFacturacion.ImpuestoIsr && r.Importe == 100m);
    }

    [Fact]
    public void Sin_tipos_resueltos_las_lineas_salen_sin_TipoProducto()
    {
        var evt = EventosContablesFacturacion.FacturaVentaTimbrada(Factura(lineas: (Guid.NewGuid(), 100m, null, null)), Timbrado);

        evt.Lineas!.Should().ContainSingle().Which.TipoProducto.Should().BeNull();
        evt.ClienteId.Should().BeNull();
    }

    [Fact]
    public void IvaCobrado_es_proporcional_al_cobro_de_la_factura()
    {
        var factura = Factura(lineas: (null, 1000m, null, null)); // total 1160, IVA 160

        EventosContablesFacturacion.IvaCobrado(factura, 1160m).Should().Be(160m);
        EventosContablesFacturacion.IvaCobrado(factura, 580m).Should().Be(80m);
    }

    [Fact]
    public void U1_6b_evento_antiguo_sin_bloque_contable_se_lee_igual()
    {
        // JSON tal como lo publicaba v1 antes de U1.6 (sin campos nuevos).
        const string antiguo = """
            {"EmpresaId":"11111111-1111-1111-1111-111111111111","OcurridoEn":"2026-09-01T12:00:00+00:00",
             "FacturaVentaId":"22222222-2222-2222-2222-222222222222","Uuid":"UUID-DEMO","Total":1160,
             "Moneda":"MXN","PedidoFacturableId":null,"ReceptorRfc":"DEM010101AB1","ReceptorNombre":"DEMO",
             "Folio":"F-1","MetodoPago":"PPD","FechaTimbrado":"2026-09-01T12:00:00+00:00"}
            """;

        var evt = JsonSerializer.Deserialize<FacturaVentaTimbradaIntegrationEvent>(antiguo)!;

        evt.Total.Should().Be(1160m);
        evt.MetodoPago.Should().Be("PPD");
        evt.EventType.Should().Be("facturacion.factura-venta.timbrada.v1");
        evt.Subtotal.Should().BeNull();
        evt.Lineas.Should().BeNull();
        evt.SucursalId.Should().BeNull();
    }

    [Fact]
    public void Nombre_y_version_de_los_eventos_no_cambian()
    {
        var factura = Factura(lineas: (null, 100m, null, null));
        EventosContablesFacturacion.FacturaVentaTimbrada(factura, Timbrado).EventType
            .Should().Be("facturacion.factura-venta.timbrada.v1");
    }
}
