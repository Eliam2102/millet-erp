using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Parsing;

namespace Millet.CuentasPorPagar.UnitTests.P8;

/// <summary>
/// 86e3zcbjw · la retención es por línea (Contabilidad, 6-nov): el CFDI de un flete puede retener 4 % solo en algunas líneas. El parser
/// toma tasa y base de los conceptos y la alerta compara contra esa base, no contra toda la factura. Datos FIX.
/// </summary>
public sealed class RetencionesPorLineaTests
{
    // Tres líneas (1,000 + 500 + 2,000); la retención de IVA 4 % va solo en la 1 y la 3: 4 % × 3,000 = 120.
    private const string FleteConRetencionParcial = """
        <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" TipoDeComprobante="I" Fecha="2026-11-06T12:00:00" Moneda="MXN" SubTotal="3500" Total="3940">
          <cfdi:Emisor Rfc="AAA010101AAA"/><cfdi:Receptor Rfc="BBB010101BBB"/>
          <cfdi:Conceptos>
            <cfdi:Concepto ClaveProdServ="78101800" Cantidad="1" ClaveUnidad="E48" Descripcion="FIX flete 1" ValorUnitario="1000" Importe="1000" ObjetoImp="02">
              <cfdi:Impuestos>
                <cfdi:Traslados><cfdi:Traslado Base="1000" Impuesto="002" TipoFactor="Tasa" TasaOCuota="0.160000" Importe="160"/></cfdi:Traslados>
                <cfdi:Retenciones><cfdi:Retencion Base="1000" Impuesto="002" TipoFactor="Tasa" TasaOCuota="0.040000" Importe="40"/></cfdi:Retenciones>
              </cfdi:Impuestos>
            </cfdi:Concepto>
            <cfdi:Concepto ClaveProdServ="78101800" Cantidad="1" ClaveUnidad="E48" Descripcion="FIX maniobra" ValorUnitario="500" Importe="500" ObjetoImp="02">
              <cfdi:Impuestos>
                <cfdi:Traslados><cfdi:Traslado Base="500" Impuesto="002" TipoFactor="Tasa" TasaOCuota="0.160000" Importe="80"/></cfdi:Traslados>
              </cfdi:Impuestos>
            </cfdi:Concepto>
            <cfdi:Concepto ClaveProdServ="78101800" Cantidad="1" ClaveUnidad="E48" Descripcion="FIX flete 2" ValorUnitario="2000" Importe="2000" ObjetoImp="02">
              <cfdi:Impuestos>
                <cfdi:Traslados><cfdi:Traslado Base="2000" Impuesto="002" TipoFactor="Tasa" TasaOCuota="0.160000" Importe="320"/></cfdi:Traslados>
                <cfdi:Retenciones><cfdi:Retencion Base="2000" Impuesto="002" TipoFactor="Tasa" TasaOCuota="0.040000" Importe="80"/></cfdi:Retenciones>
              </cfdi:Impuestos>
            </cfdi:Concepto>
          </cfdi:Conceptos>
          <cfdi:Impuestos TotalImpuestosTrasladados="560" TotalImpuestosRetenidos="120">
            <cfdi:Retenciones><cfdi:Retencion Impuesto="002" Importe="120"/></cfdi:Retenciones>
            <cfdi:Traslados><cfdi:Traslado Base="3500" Impuesto="002" TipoFactor="Tasa" TasaOCuota="0.160000" Importe="560"/></cfdi:Traslados>
          </cfdi:Impuestos>
          <cfdi:Complemento><tfd:TimbreFiscalDigital UUID="11111111-1111-1111-1111-111111111111"/></cfdi:Complemento>
        </cfdi:Comprobante>
        """;

    private static RetencionConcepto[] Fletes() =>
        [RetencionConcepto.Crear("FLETES", "FIX fletes", "002", 0.04m, "FIX fuente", "FIX propuesta fiscal")];

    [Fact]
    public void El_parser_toma_la_tasa_y_la_base_de_las_lineas_que_llevan_la_retencion()
    {
        var detalle = new XmlCfdiParser().Parsear(FleteConRetencionParcial).RetencionesDetalle;

        detalle.Should().ContainSingle().Which.Should().Be(new RetencionCfdi("002", 0.04m, 120m, 3_000m));
    }

    [Fact]
    public void La_retencion_de_solo_algunas_lineas_no_alerta_si_cuadra_con_su_base()
    {
        var detalle = new XmlCfdiParser().Parsear(FleteConRetencionParcial).RetencionesDetalle;

        ComparadorRetenciones.Alerta("FLETES", 3_500m, 120m, detalle, Fletes()).Should().BeNull(
            "4 % sobre las líneas con retención (3,000) = 120; sobre toda la factura serían 140");
        ComparadorRetenciones.Proponer(3_500m, Fletes(), detalle).Should().ContainSingle().Which.Importe.Should().Be(120m);
    }

    [Fact]
    public void Sin_desglose_por_linea_se_compara_contra_toda_la_factura_como_antes()
    {
        ComparadorRetenciones.Alerta("FLETES", 3_500m, 120m, [new RetencionCfdi("002", null, 120m)], Fletes()).Should().Contain("difieren");
        ComparadorRetenciones.Alerta("FLETES", 3_500m, 140m, [new RetencionCfdi("002", null, 140m)], Fletes()).Should().BeNull();
    }

    [Fact]
    public void Dos_tasas_del_mismo_impuesto_quedan_en_dos_renglones()
    {
        var xml = FleteConRetencionParcial
            .Replace("""TasaOCuota="0.040000" Importe="80"/>""", """TasaOCuota="0.060000" Importe="120"/>""", StringComparison.Ordinal)
            .Replace("""<cfdi:Retencion Impuesto="002" Importe="120"/>""", """<cfdi:Retencion Impuesto="002" Importe="160"/>""", StringComparison.Ordinal);

        new XmlCfdiParser().Parsear(xml).RetencionesDetalle.Should().BeEquivalentTo(new[]
        {
            new RetencionCfdi("002", 0.04m, 40m, 1_000m),
            new RetencionCfdi("002", 0.06m, 120m, 2_000m),
        });
    }

    [Fact]
    public async Task Los_conceptos_nuevos_de_la_tabla_de_Contabilidad_estan_sembrados()
    {
        await using var a = new P8Fixture();
        var reglas = a.Db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(RetencionConcepto))!.GetSeedData()
            .Select(d => ((string)d["Concepto"]!, (string)d["Impuesto"]!, (decimal)d["Tasa"]!)).ToList();

        reglas.Should().Contain(("SERVICIOS", "002", 0.06m));
        reglas.Should().Contain(("RESICO_PF_SERVICIOS", "001", 0.0125m));
        reglas.Should().Contain(("RESICO_PF_SERVICIOS", "002", 0.10666667m));
        reglas.Should().Contain(("RESICO_PF", "001", 0.0125m), "RESICO con venta de bienes sigue solo con ISR");
    }
}
