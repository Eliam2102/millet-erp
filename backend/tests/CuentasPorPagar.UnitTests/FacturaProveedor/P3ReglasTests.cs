using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Parsing;
using Millet.CuentasPorPagar.Application.FacturaProveedor.CapturarFacturaConOc;
using Millet.CuentasPorPagar.Domain.Ports.Compras;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

public sealed class P3ReglasTests
{
    [Fact]
    public void Dos_conceptos_de_la_misma_linea_OC_identifican_el_concepto_con_precio_distinto()
    {
        var (factura, oc) = Escenario();
        oc = oc with { Lineas = [oc.Lineas[0] with { Cantidad = 2 }] };
        factura = factura with { Subtotal = 70, Total = 70, Lineas = [
            factura.Lineas[0], factura.Lineas[0] with { PrecioUnitario = 50, Importe = 50 }] };
        ConciliacionFacturaOc.Conciliar(factura, oc, Tolerancia.MontoAbsoluto(0.99m), new Dictionary<Guid, decimal>())
            .Motivo.Should().Contain("Precio distinto en la línea 2").And.Contain("50.0000");
    }

    [Fact]
    public void Precios_distintos_no_se_compensan_entre_lineas_aunque_el_total_sea_exacto()
    {
        var (factura, oc) = Escenario();
        var otra = Guid.NewGuid();
        oc = oc with { Lineas = [oc.Lineas[0], oc.Lineas[0] with { Id = otra }] };
        factura = factura with { Subtotal = 40, Total = 40, Lineas = [
            factura.Lineas[0] with { PrecioUnitario = 25, Importe = 25 },
            factura.Lineas[0] with { PrecioUnitario = 15, Importe = 15, LineaOcId = otra }] };
        var resultado = ConciliacionFacturaOc.Conciliar(factura, oc, Tolerancia.MontoAbsoluto(0.99m), new Dictionary<Guid, decimal>());
        resultado.Motivo.Should().Contain("Precio distinto en la línea 1");
    }

    [Fact]
    public void Descuento_de_cabecera_se_concilia_con_la_base_pactada_de_la_OC()
    {
        var (factura, oc) = Escenario();
        factura = factura with { Descuentos = 2, Total = 18 };
        oc = oc with { Lineas = [oc.Lineas[0] with { BaseNetaUnitaria = 18 }] };
        ConciliacionFacturaOc.Conciliar(factura, oc, Tolerancia.MontoAbsoluto(0.99m), new Dictionary<Guid, decimal>())
            .Motivo.Should().BeNull();
        var alterada = factura with { Descuentos = 5, Total = 15 };
        ConciliacionFacturaOc.Conciliar(alterada, oc, Tolerancia.MontoAbsoluto(0.99m), new Dictionary<Guid, decimal>())
            .Motivo.Should().Contain("base después de descuentos");
    }

    private static (CapturarFacturaConOcCommand Factura, OrdenCompraDto Oc) Escenario()
    {
        var id = Guid.NewGuid(); var articulo = Guid.NewGuid();
        var oc = new OrdenCompraDto(Guid.NewGuid(), "OC-FICTICIA-P3", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 23.2m,
            "Autorizada", [new(id, articulo, 1, 20, 0, 1)]);
        var factura = new CapturarFacturaConOcCommand(oc.Id, oc.ProveedorId, oc.SucursalId, null, null, "P3-FICTICIA", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new(2026, 11, 9), "MXN", null, 20, 0, 0, 0, 20,
            [new(articulo, "30102400", "Material ficticio", 1, "H87", null, 20, 20, null, id, null)]);
        return (factura, oc);
    }

    [Fact]
    public void Elegible_prorratea_neto_con_impuestos_retenciones_y_NC_sin_pagar_dos_veces()
    {
        var id = Guid.NewGuid();
        var e = ElegibilidadPago.Calcular([new(id, 10, 20)], new Dictionary<Guid, decimal> { [id] = 8 },
            totalNeto: 198.67m, anticipos: 10, pagado: 20);
        e.ElegibleTotal.Should().Be(158.94m);
        e.ElegiblePendiente.Should().Be(128.94m);
        e.Retenido.Should().Be(39.73m);
        var completo = ElegibilidadPago.Calcular([new(id, 10, 20)], new Dictionary<Guid, decimal> { [id] = 10 },
            198.67m, 10, 20);
        completo.ElegiblePendiente.Should().Be(168.67m);
        completo.Retenido.Should().Be(0);
    }

    [Fact]
    public void Xml_lee_descuento_de_cabecera()
    {
        var xml = """
            <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" TipoDeComprobante="I" Fecha="2026-10-09T12:00:00" Moneda="MXN" SubTotal="100" Descuento="10" Total="90">
            <cfdi:Emisor Rfc="AAA010101AAA"/><cfdi:Receptor Rfc="BBB010101BBB"/>
            <cfdi:Complemento><tfd:TimbreFiscalDigital UUID="11111111-1111-1111-1111-111111111111"/></cfdi:Complemento></cfdi:Comprobante>
            """;
        new XmlCfdiParser().Parsear(xml).Descuentos.Should().Be(10m);
    }
}
