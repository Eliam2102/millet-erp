using Millet.Facturacion.Domain.Comprobantes;

namespace Millet.Facturacion.UnitTests.Timbrado;

public sealed class CadenaOriginalTimbreTests
{
    internal static string Xml(string leyenda = "") => $"""
        <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" NoCertificado="00000000000000000001">
          <cfdi:Complemento><tfd:TimbreFiscalDigital xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital"
          Version="1.1" UUID="11111111-1111-1111-1111-111111111111" FechaTimbrado="2026-10-20T12:13:07"
          RfcProvCertif="AAA010101AAA" {leyenda} SelloCFD="SELLO_FICTICIO" NoCertificadoSAT="00000000000000000002" /></cfdi:Complemento>
        </cfdi:Comprobante>
        """;

    [Theory]
    [InlineData("", "")]
    [InlineData("Leyenda=\"LEYENDA DEMO\"", "LEYENDA DEMO|")]
    public void Cadena_con_y_sin_leyenda(string atributo, string esperado)
    {
        Assert.Equal("||1.1|11111111-1111-1111-1111-111111111111|2026-10-20T12:13:07|AAA010101AAA|"
            + esperado + "SELLO_FICTICIO|00000000000000000002||", CadenaOriginalTimbre.Desde(Xml(atributo)));
        Assert.Equal("00000000000000000001", CadenaOriginalTimbre.CertificadoEmisorDesde(Xml(atributo)));
    }

    [Fact]
    public void Xml_con_bom_conserva_cadena_uuid_y_certificado()
    {
        var xml = "\uFEFF" + Xml();
        Assert.Equal(CadenaOriginalTimbre.Desde(Xml()), CadenaOriginalTimbre.Desde(xml));
        Assert.Equal("11111111-1111-1111-1111-111111111111", CadenaOriginalTimbre.UuidDesde(xml));
        Assert.Equal("00000000000000000001", CadenaOriginalTimbre.CertificadoEmisorDesde(xml));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<cfdi fake='true'/>")]
    [InlineData("<malformado")]
    [InlineData("<!DOCTYPE x [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><x>&x;</x>")]
    public void No_inventa_datos_ante_xml_incompleto_o_inseguro(string? xml)
    {
        Assert.Null(CadenaOriginalTimbre.Desde(xml));
        Assert.Null(CadenaOriginalTimbre.CertificadoEmisorDesde(xml));
    }
}
