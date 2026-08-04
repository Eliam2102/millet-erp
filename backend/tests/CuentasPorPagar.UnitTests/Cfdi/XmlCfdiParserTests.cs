using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Parsing;

namespace Millet.CuentasPorPagar.UnitTests.Cfdi;

public sealed class XmlCfdiParserTests
{
    private readonly XmlCfdiParser _parser = new();

    [Fact]
    public void Parsear_cfdi_ingreso_basico_extrae_cabecera_completa()
    {
        var xml = BuildCfdi(
            tipo: "I",
            uuid: "5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B",
            rfcEmisor: "PROV010101AAA",
            rfcReceptor: "MIL010101AAA",
            total: "1160.00",
            subtotal: "1000.00",
            impuestos: "160.00");

        var datos = _parser.Parsear(xml);

        datos.UuidCfdi.Should().Be("5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B");
        datos.RfcEmisor.Should().Be("PROV010101AAA");
        datos.RfcReceptor.Should().Be("MIL010101AAA");
        datos.Tipo.Should().Be(TipoCfdi.Ingreso);
        datos.Total.Should().Be(1160.00m);
        datos.Subtotal.Should().Be(1000.00m);
        datos.ImpuestosTrasladados.Should().Be(160.00m);
        datos.Retenciones.Should().Be(0m);
        datos.Moneda.Should().Be("MXN");
        datos.Folio.Should().Be("F123");
        datos.Serie.Should().Be("A");
        datos.Lineas.Should().HaveCount(1);
        datos.Lineas[0].Descripcion.Should().Be("Servicio de prueba");
    }

    [Fact]
    public void Parsear_cfdi_egreso_devuelve_tipo_egreso()
    {
        var xml = BuildCfdi(tipo: "E", uuid: "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");
        var datos = _parser.Parsear(xml);
        datos.Tipo.Should().Be(TipoCfdi.Egreso);
    }

    [Fact]
    public void Parsear_cfdi_pago_devuelve_tipo_pago()
    {
        var xml = BuildCfdi(tipo: "P", uuid: "11111111-2222-3333-4444-555555555555");
        var datos = _parser.Parsear(xml);
        datos.Tipo.Should().Be(TipoCfdi.Pago);
    }

    [Fact]
    public void Parsear_extrae_MetodoPago_y_null_si_ausente()
    {
        // TES-PR8 [T-G11]: el MetodoPago del Comprobante viaja hasta
        // Tesorería vía pasivo.autorizado-para-pago.v1.
        var conMetodo = _parser.Parsear(BuildCfdi(metodoPago: "PPD"));
        conMetodo.MetodoPago.Should().Be("PPD");

        var sinMetodo = _parser.Parsear(BuildCfdi());
        sinMetodo.MetodoPago.Should().BeNull();
    }

    [Fact]
    public void Parsear_extrae_CfdiRelacionados_con_tipo_y_uuids()
    {
        // NC de proveedor (tipo E) con relación 01 hacia la factura origen.
        var xml = BuildCfdi(
            tipo: "E",
            uuid: "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE",
            cfdiRelacionados: """
                <cfdi:CfdiRelacionados TipoRelacion="01">
                  <cfdi:CfdiRelacionado UUID="5fb0f1c2-3e2a-4f0f-9e2e-2c5c2b5c1a2b" />
                  <cfdi:CfdiRelacionado UUID="11111111-2222-3333-4444-555555555555" />
                </cfdi:CfdiRelacionados>
                """);

        var datos = _parser.Parsear(xml);

        datos.CfdiRelacionados.Should().NotBeNull().And.HaveCount(1);
        datos.CfdiRelacionados![0].TipoRelacion.Should().Be("01");
        datos.CfdiRelacionados[0].Uuids.Should().Equal(
            "5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B",
            "11111111-2222-3333-4444-555555555555");
    }

    [Fact]
    public void Parsear_extrae_multiples_nodos_CfdiRelacionados()
    {
        var xml = BuildCfdi(cfdiRelacionados: """
            <cfdi:CfdiRelacionados TipoRelacion="07">
              <cfdi:CfdiRelacionado UUID="AAAAAAAA-0000-0000-0000-000000000001" />
            </cfdi:CfdiRelacionados>
            <cfdi:CfdiRelacionados TipoRelacion="03">
              <cfdi:CfdiRelacionado UUID="AAAAAAAA-0000-0000-0000-000000000002" />
            </cfdi:CfdiRelacionados>
            """);

        var datos = _parser.Parsear(xml);

        datos.CfdiRelacionados.Should().HaveCount(2);
        datos.CfdiRelacionados![0].TipoRelacion.Should().Be("07");
        datos.CfdiRelacionados[1].TipoRelacion.Should().Be("03");
    }

    [Fact]
    public void Parsear_sin_CfdiRelacionados_devuelve_null()
    {
        var datos = _parser.Parsear(BuildCfdi());
        datos.CfdiRelacionados.Should().BeNull();
    }

    [Fact]
    public void Parsear_xml_malformado_lanza_CfdiParseException()
    {
        var act = () => _parser.Parsear("<not-a-cfdi>");
        act.Should().Throw<CfdiParseException>()
            .Where(e => e.Codigo == "CFDI_XML_MALFORMADO");
    }

    [Fact]
    public void Parsear_xml_sin_timbre_lanza_CfdiParseException()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4"
                              Version="4.0" TipoDeComprobante="I" Total="0" SubTotal="0" Fecha="2026-05-22T10:00:00" Moneda="MXN">
              <cfdi:Emisor Rfc="PROV010101AAA" Nombre="Proveedor" />
              <cfdi:Receptor Rfc="MIL010101AAA" Nombre="Millet" />
            </cfdi:Comprobante>
            """;

        var act = () => _parser.Parsear(xml);
        act.Should().Throw<CfdiParseException>()
            .Where(e => e.Codigo == "CFDI_SIN_TIMBRE");
    }

    [Fact]
    public void Parsear_version_distinta_a_40_lanza_CfdiParseException()
    {
        var xml = BuildCfdi(version: "3.3", uuid: "11111111-2222-3333-4444-555555555555");
        var act = () => _parser.Parsear(xml);
        act.Should().Throw<CfdiParseException>()
            .Where(e => e.Codigo == "CFDI_VERSION_NO_SOPORTADA");
    }

    [Fact]
    public void Parsear_acepta_TipoCambio_decimal()
    {
        var xml = BuildCfdi(
            uuid: "11111111-2222-3333-4444-555555555555",
            moneda: "USD",
            tipoCambio: "18.5432");
        var datos = _parser.Parsear(xml);
        datos.Moneda.Should().Be("USD");
        datos.TipoCambio.Should().Be(18.5432m);
    }

    [Fact]
    public void Parsear_normaliza_fecha_a_utc()
    {
        var xml = BuildCfdi(
            uuid: "11111111-2222-3333-4444-555555555555",
            fecha: "2026-05-22T10:00:00");
        var datos = _parser.Parsear(xml);
        datos.FechaCfdi.Offset.Should().Be(TimeSpan.Zero);
    }

    private static string BuildCfdi(
        string version = "4.0",
        string tipo = "I",
        string uuid = "5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B",
        string rfcEmisor = "PROV010101AAA",
        string rfcReceptor = "MIL010101AAA",
        string fecha = "2026-05-22T10:00:00",
        string total = "1160.00",
        string subtotal = "1000.00",
        string impuestos = "160.00",
        string moneda = "MXN",
        string? tipoCambio = null,
        string? metodoPago = null,
        string? cfdiRelacionados = null)
    {
        var tcAttr = tipoCambio is null ? "" : $@" TipoCambio=""{tipoCambio}""";
        var mpAttr = metodoPago is null ? "" : $@" MetodoPago=""{metodoPago}""";
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4"
                              Version="{version}" TipoDeComprobante="{tipo}"
                              Serie="A" Folio="F123"
                              Fecha="{fecha}" Total="{total}" SubTotal="{subtotal}"
                              Moneda="{moneda}"{tcAttr}{mpAttr}>
              {cfdiRelacionados}
              <cfdi:Emisor Rfc="{rfcEmisor}" Nombre="Proveedor SA" />
              <cfdi:Receptor Rfc="{rfcReceptor}" Nombre="Millet SA" />
              <cfdi:Conceptos>
                <cfdi:Concepto ClaveProdServ="01010101" Cantidad="1" ClaveUnidad="E48"
                               Descripcion="Servicio de prueba" ValorUnitario="{subtotal}" Importe="{subtotal}" />
              </cfdi:Conceptos>
              <cfdi:Impuestos TotalImpuestosTrasladados="{impuestos}" />
              <cfdi:Complemento>
                <tfd:TimbreFiscalDigital xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital"
                                          UUID="{uuid}" FechaTimbrado="{fecha}" />
              </cfdi:Complemento>
            </cfdi:Comprobante>
            """;
    }
}
