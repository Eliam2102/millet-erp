using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Millet.Facturacion.Application.Timbrado.Adopcion;
using Millet.Facturacion.Domain.Comprobantes;

namespace Millet.Facturacion.UnitTests.TestDoubles;

/// <summary>
/// M-284 (QA O1): CFDI 4.0 timbrado y firmado de verdad para pruebas. Un certificado ficticio del emisor sella
/// la cadena original (XSLT del SAT) y otro ficticio «del SAT» sella el Timbre Fiscal Digital. Datos ficticios.
/// </summary>
public static class CfdiFirmadoDePrueba
{
    public const string NoCertificadoEmisor = "30001000000500009999";
    public const string NoCertificadoSat = "30001000000500008888";

    private static readonly Lazy<(RSA Llave, X509Certificate2 Cert)> Emisor = new(() => Crear("CN=Emisor de prueba", NoCertificadoEmisor));
    private static readonly Lazy<(RSA Llave, X509Certificate2 Cert)> Sat = new(() => Crear("CN=SAT de prueba", NoCertificadoSat));

    /// <summary>Proveedor que entrega el certificado «del SAT» de prueba (o ninguno).</summary>
    public sealed class CertificadosSat(bool disponible = true) : ICertificadoSatProvider
    {
        public Task<X509Certificate2?> ObtenerAsync(string noCertificado, CancellationToken cancellationToken) =>
            Task.FromResult(disponible && noCertificado == NoCertificadoSat
                ? X509CertificateLoader.LoadCertificate(Sat.Value.Cert.RawData)
                : null);
    }

    /// <summary>
    /// XML timbrado y sellado. <paramref name="selloCfdTimbre"/> pone otro SelloCFD en el timbre (timbre de otro CFDI).
    /// </summary>
    public static string Xml(string serie = "MERA", string folio = "15230", string total = "11600.00",
        string rfcEmisor = "MIL010101AAA", string rfcReceptor = "AAA010101AAA", string tipo = "I",
        string uuid = "5E2D6F33-0A4B-4C1D-9E8F-112233445566", bool conTimbre = true, string? selloCfdTimbre = null)
    {
        var certificado = Convert.ToBase64String(Emisor.Value.Cert.RawData);
        string Comprobante(string sello) => $"""
            <?xml version="1.0" encoding="utf-8"?>
            <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital"
                Version="4.0" Serie="{serie}" Folio="{folio}" Fecha="2026-11-11T14:58:00" FormaPago="03" NoCertificado="{NoCertificadoEmisor}"
                SubTotal="10000.00" Moneda="MXN" Total="{total}" TipoDeComprobante="{tipo}" Exportacion="01" MetodoPago="PUE"
                LugarExpedicion="76120" Sello="{sello}" Certificado="{certificado}">
              <cfdi:Emisor Rfc="{rfcEmisor}" Nombre="EMISOR DE PRUEBA" RegimenFiscal="601" />
              <cfdi:Receptor Rfc="{rfcReceptor}" Nombre="CLIENTE DE PRUEBA" DomicilioFiscalReceptor="97000" RegimenFiscalReceptor="601" UsoCFDI="G03" />
              <cfdi:Conceptos>
                <cfdi:Concepto ClaveProdServ="30171500" Cantidad="1" ClaveUnidad="H87" Descripcion="Vidrio" ValorUnitario="10000.00" Importe="10000.00" ObjetoImp="02" />
              </cfdi:Conceptos>
              <cfdi:Complemento>@TIMBRE@</cfdi:Complemento>
            </cfdi:Comprobante>
            """;

        var cadena = VerificadorSelloCfdi.CadenaOriginal(Comprobante("").Replace("@TIMBRE@", ""));
        var sello = Firmar(Emisor.Value.Llave, cadena);
        var xml = Comprobante(sello);
        if (!conTimbre) return xml.Replace("@TIMBRE@", "");

        string Timbre(string selloSat) =>
            $"""<tfd:TimbreFiscalDigital Version="1.1" UUID="{uuid}" FechaTimbrado="2026-11-11T14:59:00" RfcProvCertif="SPR190613I52" SelloCFD="{selloCfdTimbre ?? sello}" NoCertificadoSAT="{NoCertificadoSat}" SelloSAT="{selloSat}" />""";
        var cadenaTimbre = CadenaOriginalTimbre.Desde(xml.Replace("@TIMBRE@", Timbre("X")))!;
        return xml.Replace("@TIMBRE@", Timbre(Firmar(Sat.Value.Llave, cadenaTimbre)));
    }

    private static string Firmar(RSA llave, string cadena) =>
        Convert.ToBase64String(llave.SignData(Encoding.UTF8.GetBytes(cadena), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

    private static (RSA, X509Certificate2) Crear(string sujeto, string numero)
    {
        var llave = RSA.Create(2048);
        var solicitud = new CertificateRequest(sujeto, llave, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = solicitud.Create(new X500DistinguishedName(sujeto), X509SignatureGenerator.CreateForRSA(llave, RSASignaturePadding.Pkcs1),
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(4), Encoding.ASCII.GetBytes(numero));
        return (llave, cert);
    }
}
