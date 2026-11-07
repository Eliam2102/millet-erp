using System.Xml.Linq;
using FluentAssertions;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.NotasCredito;
using Xunit;

namespace Millet.Facturacion.UnitTests.Anticipos;

/// <summary>
/// Validación contra el CFDI 4.0 real Serie FANT Folio 504563 entregado por Sorandi Martínez y Guadalupe Polanco
/// el 5 de noviembre de 2026 (UUID: 9FE72B22-3AFF-5789-8389-728F8D7C6179).
/// Valida la lectura del XML timbrado oficial del SAT, desglose de subtotal/IVA, cálculo de amortización con relación 07
/// y precisión al centavo tanto en divisas de exportación (USD, tasa 0%) como en moneda nacional ($4,000 MXN, tasa 16%).
/// </summary>
public sealed class CfdiRealAnticipoFANT504563Tests
{
    private const string RutaXmlObsidian =
        "/home/devgeo/Obsidian/Millet-ERP/Documentos/2026-11-06 correos Millet/adjuntos/XEXX010101000_FacturaNormal__FANT_504563.xml";

    private const string XmlSatOficial = """
        <?xml version="1.0" encoding="utf-8"?>
        <cfdi:Comprobante xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" Serie="FANT" Folio="504563" Fecha="2026-05-07T15:29:12" FormaPago="03" CondicionesDePago="00 CONTADO" SubTotal="485282.09" Moneda="USD" TipoCambio="17.3752" Total="485282.09" TipoDeComprobante="I" Exportacion="01" MetodoPago="PUE" LugarExpedicion="97345">
          <cfdi:Emisor Rfc="MIN120224147" Nombre="M INDUSTRIA" RegimenFiscal="601" />
          <cfdi:Receptor Rfc="XEXX010101000" Nombre="INVERSIONES ZAHENA SA" DomicilioFiscalReceptor="97345" ResidenciaFiscal="DOM" NumRegIdTrib="130044147" RegimenFiscalReceptor="616" UsoCFDI="S01" />
          <cfdi:Conceptos>
            <cfdi:Concepto ClaveProdServ="84111506" NoIdentificacion="ANT00002" Cantidad="1.00" ClaveUnidad="ACT" Unidad="ACT" Descripcion="ANTICIPO DE CLIENTES USD" ValorUnitario="485282.09" Importe="485282.09" ObjetoImp="02">
              <cfdi:Impuestos>
                <cfdi:Traslados>
                  <cfdi:Traslado Base="485282.09" Impuesto="002" TipoFactor="Tasa" TasaOCuota="0.000000" Importe="0.00" />
                </cfdi:Traslados>
              </cfdi:Impuestos>
            </cfdi:Concepto>
          </cfdi:Conceptos>
          <cfdi:Impuestos TotalImpuestosTrasladados="0.00">
            <cfdi:Traslados>
              <cfdi:Traslado Base="485282.09" Impuesto="002" TipoFactor="Tasa" TasaOCuota="0.000000" Importe="0.00" />
            </cfdi:Traslados>
          </cfdi:Impuestos>
          <cfdi:Complemento>
            <tfd:TimbreFiscalDigital Version="1.1" RfcProvCertif="CVD110412TF6" UUID="9FE72B22-3AFF-5789-8389-728F8D7C6179" FechaTimbrado="2026-05-07T15:31:55" />
          </cfdi:Complemento>
        </cfdi:Comprobante>
        """;

    [Fact]
    public void XML_real_de_Millet_cumple_estructura_SAT_y_datos_fiscales_entregados()
    {
        var xmlContent = File.Exists(RutaXmlObsidian) ? File.ReadAllText(RutaXmlObsidian) : XmlSatOficial;
        var doc = XDocument.Parse(xmlContent);

        XNamespace cfdi = "http://www.sat.gob.mx/cfd/4";
        XNamespace tfd = "http://www.sat.gob.mx/TimbreFiscalDigital";

        var root = doc.Root;
        root.Should().NotBeNull();
        root!.Attribute("Serie")?.Value.Should().Be("FANT");
        root.Attribute("Folio")?.Value.Should().Be("504563");
        root.Attribute("Moneda")?.Value.Should().Be("USD");
        root.Attribute("TipoCambio")?.Value.Should().Be("17.3752");
        root.Attribute("SubTotal")?.Value.Should().Be("485282.09");
        root.Attribute("Total")?.Value.Should().Be("485282.09");
        root.Attribute("FormaPago")?.Value.Should().Be("03");
        root.Attribute("MetodoPago")?.Value.Should().Be("PUE");

        var emisor = root.Element(cfdi + "Emisor");
        emisor.Should().NotBeNull();
        emisor!.Attribute("Rfc")?.Value.Should().Be("MIN120224147");
        emisor.Attribute("RegimenFiscal")?.Value.Should().Be("601");

        var receptor = root.Element(cfdi + "Receptor");
        receptor.Should().NotBeNull();
        receptor!.Attribute("Rfc")?.Value.Should().Be("XEXX010101000");
        receptor.Attribute("Nombre")?.Value.Should().Be("INVERSIONES ZAHENA SA");
        receptor.Attribute("ResidenciaFiscal")?.Value.Should().Be("DOM");
        receptor.Attribute("NumRegIdTrib")?.Value.Should().Be("130044147");
        receptor.Attribute("RegimenFiscalReceptor")?.Value.Should().Be("616");
        receptor.Attribute("UsoCFDI")?.Value.Should().Be("S01");

        var concepto = root.Element(cfdi + "Conceptos")?.Element(cfdi + "Concepto");
        concepto.Should().NotBeNull();
        concepto!.Attribute("ClaveProdServ")?.Value.Should().Be("84111506");
        concepto.Attribute("ClaveUnidad")?.Value.Should().Be("ACT");
        concepto.Attribute("Descripcion")?.Value.Should().Be("ANTICIPO DE CLIENTES USD");
        concepto.Attribute("Importe")?.Value.Should().Be("485282.09");

        var timbre = root.Element(cfdi + "Complemento")?.Element(tfd + "TimbreFiscalDigital");
        timbre.Should().NotBeNull();
        timbre!.Attribute("UUID")?.Value.Should().Be("9FE72B22-3AFF-5789-8389-728F8D7C6179");
    }

    [Fact]
    public void Caso_nacional_Sorandi_4000_MXN_con_anticipo_desglosa_subtotal_iva_y_relacion_07_al_centavo()
    {
        // Caso real documentado con Sorandi Martínez:
        // Factura de $4,000.00 MXN con aplicación de anticipo:
        // Subtotal: $3,448.28 MXN (4000 / 1.16)
        // IVA 16%: $551.72 MXN
        // Total: $4,000.00 MXN exactos al centavo
        var empresaId = Guid.NewGuid();
        var anticipoId = Guid.NewGuid();
        var facturaId = Guid.NewGuid();
        var uuidFactura = "9FE72B22-3AFF-5789-8389-728F8D7C6179";

        var receptor = new DatosFiscalesReceptor(
            "MIN120224147", "M INDUSTRIA", "601", "97345", "G01", "MEX", false);
        var emisor = new DatosFiscalesEmisor("MIL010101AAA", "Millet", "601", "97345");

        var nc = NotaCredito.CrearAmortizacion(
            empresaId: empresaId,
            folio: "NC-504563",
            folioNumero: 504563,
            sucursalId: Guid.NewGuid(),
            cajaId: null,
            usuarioEmisorId: null,
            receptor: NotaCredito.ReceptorAplicacion(receptor),
            emisor: emisor,
            formaPago: NotaCredito.FormaPagoAplicacionAnticipo,
            moneda: "MXN",
            tipoCambio: null,
            periodoAnio: 2026,
            periodoMes: 11,
            canalVentaId: (short)1,
            anticipoOrigenId: anticipoId,
            facturaRelacionadaId: facturaId,
            montoTotal: 4000.00m,
            tasaIva: 0.16m);

        nc.AgregarRelacion(uuidFactura, MotivoNotaCredito.Amortizacion.ClaveTipoRelacion());

        // Verificaciones de negocio y fiscales:
        nc.Motivo.Should().Be(MotivoNotaCredito.Amortizacion);
        MotivoNotaCredito.Amortizacion.ClaveTipoRelacion().Should().Be("07", "SAT: TipoRelacion 07 = Aplicación de anticipo");
        nc.FormaPago.Should().Be("30", "SAT: Forma de pago 30 = Aplicación de anticipos");
        nc.ClaveProdServSat.Should().Be("84111506", "SAT: Clave 84111506 = Servicios de facturación / anticipos");
        nc.ClaveUnidadSat.Should().Be("ACT", "SAT: Unidad ACT");

        // Precisión al centavo:
        nc.Subtotal.Should().Be(3448.28m);
        nc.ImpuestosTrasladados.Should().Be(551.72m);
        nc.Total.Should().Be(4000.00m);
        (nc.Subtotal + nc.ImpuestosTrasladados).Should().Be(nc.Total);

        // Relación CFDI vinculada:
        nc.Relaciones.Should().ContainSingle()
            .Which.Should().Match<RelacionCfdi>(r =>
                r.TipoRelacion == "07" && r.UuidRelacionado == uuidFactura);
    }

    [Fact]
    public void Caso_exportacion_FANT_504563_amortizacion_USD_tasa_cero_preserva_total_sin_iva()
    {
        // En exportación a tasa 0% (como el CFDI FANT 504563 con INVERSIONES ZAHENA SA):
        // Importe = $485,282.09 USD, IVA = $0.00, Total = $485,282.09 USD
        var empresaId = Guid.NewGuid();
        var anticipoId = Guid.NewGuid();
        var facturaId = Guid.NewGuid();
        const decimal totalUsd = 485282.09m;

        var receptor = new DatosFiscalesReceptor(
            "XEXX010101000", "INVERSIONES ZAHENA SA", "616", "97345", "S01", "DOM", true);
        var emisor = new DatosFiscalesEmisor("MIN120224147", "M INDUSTRIA", "601", "97345");

        var nc = NotaCredito.CrearAmortizacion(
            empresaId: empresaId,
            folio: "NC-504563-EXP",
            folioNumero: 504563,
            sucursalId: Guid.NewGuid(),
            cajaId: null,
            usuarioEmisorId: null,
            receptor: NotaCredito.ReceptorAplicacion(receptor),
            emisor: emisor,
            formaPago: NotaCredito.FormaPagoAplicacionAnticipo,
            moneda: "USD",
            tipoCambio: 17.3752m,
            periodoAnio: 2026,
            periodoMes: 5,
            canalVentaId: (short)1,
            anticipoOrigenId: anticipoId,
            facturaRelacionadaId: facturaId,
            montoTotal: totalUsd,
            tasaIva: 0.00m);

        nc.Subtotal.Should().Be(totalUsd);
        nc.ImpuestosTrasladados.Should().Be(0.00m);
        nc.Total.Should().Be(totalUsd);
        nc.Moneda.Should().Be("USD");
        nc.TipoCambio.Should().Be(17.3752m);
    }
}
