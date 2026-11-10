using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Parsing;

namespace Millet.CuentasPorPagar.UnitTests.Cfdi;

/// <summary>
/// M-209 (86e3zr7mp): el parser suma por tratamiento los traslados de IVA de los conceptos del CFDI (16 %, 8 %, 0 %, exento) y los
/// conceptos no objeto de impuesto, para que Contabilidad separe el IVA y la DIOT tenga las bases. Datos FIX.
/// </summary>
public sealed class M209TrasladosIvaParserTests
{
    private readonly XmlCfdiParser _parser = new();

    private static string Xml(string conceptos, string totalTrasladados) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0" TipoDeComprobante="I" Serie="A" Folio="F209"
                          Fecha="2026-11-05T10:00:00" Total="0" SubTotal="0" Moneda="MXN">
          <cfdi:Emisor Rfc="AAA010101AAA" Nombre="Proveedor FIX" />
          <cfdi:Receptor Rfc="MIL010101AAA" Nombre="Millet FIX" />
          <cfdi:Conceptos>{conceptos}</cfdi:Conceptos>
          <cfdi:Impuestos TotalImpuestosTrasladados="{totalTrasladados}" />
          <cfdi:Complemento>
            <tfd:TimbreFiscalDigital xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" UUID="11111111-2222-3333-4444-555555555209"
                                     FechaTimbrado="2026-11-05T10:00:00" />
          </cfdi:Complemento>
        </cfdi:Comprobante>
        """;

    private static string Concepto(string importe, string objetoImp, string? traslado) => $"""
        <cfdi:Concepto ClaveProdServ="01010101" Cantidad="1" ClaveUnidad="E48" Descripcion="FIX" ValorUnitario="{importe}" Importe="{importe}"
                       ObjetoImp="{objetoImp}">
          {(traslado is null ? "" : $"<cfdi:Impuestos><cfdi:Traslados>{traslado}</cfdi:Traslados></cfdi:Impuestos>")}
        </cfdi:Concepto>
        """;

    [Fact]
    public void CA1_separa_la_base_al_16_y_la_exenta()
    {
        var xml = Xml(
            Concepto("6000.00", "02", """<cfdi:Traslado Base="6000.00" Impuesto="002" TipoFactor="Tasa" TasaOCuota="0.160000" Importe="960.00" />""")
            + Concepto("4000.00", "02", """<cfdi:Traslado Base="4000.00" Impuesto="002" TipoFactor="Tasa" TasaOCuota="0.160000" Importe="640.00" />""")
            + Concepto("2500.00", "02", """<cfdi:Traslado Base="2500.00" Impuesto="002" TipoFactor="Exento" />"""),
            "1600.00");

        var traslados = _parser.Parsear(xml).TrasladosIva;

        traslados.Should().BeEquivalentTo(new[] {
            new TrasladoIvaCfdi(TrasladoIvaCfdi.Tasa16, 10_000m, 1_600m),
            new TrasladoIvaCfdi(TrasladoIvaCfdi.Exento, 2_500m, 0m),
        });
    }

    [Fact]
    public void Tasa_8_tasa_0_y_no_objeto_van_por_separado()
    {
        var xml = Xml(
            Concepto("5000.00", "02", """<cfdi:Traslado Base="5000.00" Impuesto="002" TipoFactor="Tasa" TasaOCuota="0.080000" Importe="400.00" />""")
            + Concepto("1000.00", "02", """<cfdi:Traslado Base="1000.00" Impuesto="002" TipoFactor="Tasa" TasaOCuota="0.000000" Importe="0.00" />""")
            + Concepto("300.00", "01", null),
            "400.00");

        _parser.Parsear(xml).TrasladosIva.Should().BeEquivalentTo(new[] {
            new TrasladoIvaCfdi(TrasladoIvaCfdi.Tasa8, 5_000m, 400m),
            new TrasladoIvaCfdi(TrasladoIvaCfdi.Tasa0, 1_000m, 0m),
            new TrasladoIvaCfdi(TrasladoIvaCfdi.NoObjeto, 300m, 0m),
        });
    }

    [Fact]
    public void Sin_traslados_por_concepto_no_hay_detalle()
    {
        _parser.Parsear(Xml(Concepto("1000.00", "02", null), "160.00")).TrasladosIva.Should().BeNull();
    }
}
