using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
using Millet.Facturacion.Domain.Comprobantes;

namespace Millet.Facturacion.Application.Timbrado.Adopcion;

/// <summary>Resultado de una verificación de sello: válido, inválido con motivo, o no verificable.</summary>
public sealed record ResultadoSello(bool Valido, string Detalle, bool Verificable = true);

/// <summary>Certificado del SAT (del PAC) con que se selló un Timbre Fiscal Digital, por su número.</summary>
public interface ICertificadoSatProvider
{
    /// <summary>El certificado, o null si no se pudo obtener (no existe en el repositorio del SAT o sin red).</summary>
    Task<X509Certificate2?> ObtenerAsync(string noCertificado, CancellationToken cancellationToken);
}

/// <summary>
/// M-284 (QA O1): verificación criptográfica de un CFDI 4.0 timbrado que se adopta.
/// <list type="bullet">
///   <item><b>Sello del emisor:</b> el atributo <c>Sello</c> sobre la cadena original del XML recibido
///   (XSLT oficial del SAT 4.0 y sus complementos, embebidos), con el certificado del emisor que trae el
///   propio XML (<c>Certificado</c>, cuyo número debe ser <c>NoCertificado</c>). Detecta cualquier cambio en
///   folio, serie, conceptos, fechas o importes.</item>
///   <item><b>Sello del SAT:</b> el <c>SelloSAT</c> del Timbre Fiscal Digital 1.1 sobre su cadena original,
///   con el certificado del SAT por <c>NoCertificadoSAT</c>.</item>
/// </list>
/// SHA-256 con RSA PKCS#1 v1.5, como lo exige el Anexo 20.
/// </summary>
public static class VerificadorSelloCfdi
{
    private const string BaseSat = "http://www.sat.gob.mx/sitio_internet/cfd/";
    private const string Principal = "4/cadenaoriginal_4_0/cadenaoriginal_4_0.xslt";
    private static readonly XNamespace Cfdi = "http://www.sat.gob.mx/cfd/4";
    private static readonly XNamespace Tfd = "http://www.sat.gob.mx/TimbreFiscalDigital";

    private static readonly Lazy<XslCompiledTransform> Transformacion = new(Compilar, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Cadena original del CFDI 4.0 con el XSLT oficial del SAT.</summary>
    public static string CadenaOriginal(string xml)
    {
        using var entrada = XmlReader.Create(new StringReader(xml.TrimStart('﻿')), LectorSeguro());
        using var salida = new StringWriter();
        Transformacion.Value.Transform(entrada, null, salida);
        return salida.ToString();
    }

    /// <summary>Verifica el <c>Sello</c> del comprobante con el certificado del emisor incluido en el XML.</summary>
    public static ResultadoSello VerificarSelloEmisor(string xml)
    {
        XElement raiz;
        try
        {
            raiz = Leer(xml);
        }
        catch (XmlException)
        {
            return new(false, "El archivo no es un XML válido.");
        }
        var sello = (string?)raiz.Attribute("Sello");
        var certificado = (string?)raiz.Attribute("Certificado");
        var noCertificado = (string?)raiz.Attribute("NoCertificado");
        if (string.IsNullOrWhiteSpace(sello) || string.IsNullOrWhiteSpace(certificado))
            return new(false, "El XML no trae el sello o el certificado del emisor.");

        X509Certificate2 cert;
        try
        {
            cert = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(certificado.Trim()));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return new(false, "El certificado del emisor del XML no es válido.");
        }
        using (cert)
        {
            if (!string.IsNullOrWhiteSpace(noCertificado) && NumeroCertificado(cert) != noCertificado.Trim())
                return new(false, $"El certificado del XML ({NumeroCertificado(cert)}) no es el NoCertificado {noCertificado}.");

            string cadena;
            try
            {
                cadena = CadenaOriginal(xml);
            }
            catch (XsltException ex)
            {
                return new(false, $"No se pudo armar la cadena original del XML: {ex.Message}");
            }
            return Verificar(cert, cadena, sello)
                ? new(true, "Válido")
                : new(false, "El sello del emisor no corresponde al contenido del XML; el archivo fue modificado.");
        }
    }

    /// <summary>Verifica el <c>SelloSAT</c> del Timbre Fiscal Digital con el certificado del SAT.</summary>
    public static async Task<ResultadoSello> VerificarSelloSatAsync(
        string xml, ICertificadoSatProvider? certificados, CancellationToken cancellationToken)
    {
        var timbre = Leer(xml).Element(Cfdi + "Complemento")?.Element(Tfd + "TimbreFiscalDigital");
        var selloSat = (string?)timbre?.Attribute("SelloSAT");
        var noCertificadoSat = (string?)timbre?.Attribute("NoCertificadoSAT");
        var cadena = CadenaOriginalTimbre.Desde(xml);
        if (timbre is null || string.IsNullOrWhiteSpace(selloSat) || string.IsNullOrWhiteSpace(noCertificadoSat) || cadena is null)
            return new(false, "El Timbre Fiscal Digital está incompleto (sin SelloSAT, NoCertificadoSAT o datos de su cadena).");

        using var cert = certificados is null ? null : await certificados.ObtenerAsync(noCertificadoSat.Trim(), cancellationToken);
        if (cert is null)
            return new(false, $"No se pudo obtener el certificado del SAT {noCertificadoSat}.", Verificable: false);
        return Verificar(cert, cadena, selloSat)
            ? new(true, "Válido")
            : new(false, "El sello del SAT no corresponde al Timbre Fiscal Digital; el timbre fue modificado.");
    }

    /// <summary>Número de certificado SAT: el número de serie (hex) leído como texto ASCII.</summary>
    public static string NumeroCertificado(X509Certificate2 cert)
    {
        var hex = cert.SerialNumber;
        var bytes = Convert.FromHexString(hex.Length % 2 == 0 ? hex : "0" + hex);
        return bytes.All(b => b is >= (byte)'0' and <= (byte)'9') ? Encoding.ASCII.GetString(bytes) : hex;
    }

    private static bool Verificar(X509Certificate2 cert, string cadena, string selloBase64)
    {
        using var rsa = cert.GetRSAPublicKey();
        if (rsa is null) return false;
        try
        {
            return rsa.VerifyData(Encoding.UTF8.GetBytes(cadena), Convert.FromBase64String(selloBase64.Trim()),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static XElement Leer(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml.TrimStart('﻿')), LectorSeguro());
        return XDocument.Load(reader).Root ?? throw new XmlException("Sin raíz.");
    }

    private static XmlReaderSettings LectorSeguro() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = LectorCfdiTimbrado.TamanoMaximo,
    };

    private static XslCompiledTransform Compilar()
    {
        var resolver = new ResolverSat();
        var xslt = new XslCompiledTransform();
        using var principal = XmlReader.Create(ResolverSat.Abrir(Principal), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit },
            BaseSat + Principal);
        xslt.Load(principal, new XsltSettings(enableDocumentFunction: false, enableScript: false), resolver);
        return xslt;
    }

    /// <summary>Resuelve los <c>xsl:include</c> del SAT con los XSLT embebidos; nunca sale a internet.</summary>
    private sealed class ResolverSat : XmlResolver
    {
        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            var url = absoluteUri.ToString();
            var https = url.Replace("https://", "http://", StringComparison.OrdinalIgnoreCase);
            if (!https.StartsWith(BaseSat, StringComparison.OrdinalIgnoreCase))
                throw new XsltException($"Recurso no permitido para la cadena original: {url}");
            return Abrir(https[BaseSat.Length..]);
        }

        public static Stream Abrir(string ruta)
        {
            var ensamblado = typeof(VerificadorSelloCfdi).Assembly;
            var nombre = "SatXslt/" + ruta;
            var recurso = ensamblado.GetManifestResourceStream(nombre)
                ?? ensamblado.GetManifestResourceStream(nombre.Replace('/', '\\'))
                ?? ensamblado.GetManifestResourceNames()
                    .Where(n => string.Equals(n.Replace('\\', '/'), nombre, StringComparison.OrdinalIgnoreCase))
                    .Select(n => ensamblado.GetManifestResourceStream(n)).FirstOrDefault();
            return recurso ?? throw new XsltException($"Falta el XSLT del SAT embebido: {ruta}");
        }
    }
}
