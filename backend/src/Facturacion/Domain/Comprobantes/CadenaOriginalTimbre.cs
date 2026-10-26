using System.Xml;
using System.Xml.Linq;

namespace Millet.Facturacion.Domain.Comprobantes;

/// <summary>Lectura pura del CFDI timbrado; no incorpora datos comerciales de impresión.</summary>
public static class CadenaOriginalTimbre
{
    private static readonly XNamespace Cfdi = "http://www.sat.gob.mx/cfd/4";
    private static readonly XNamespace Tfd = "http://www.sat.gob.mx/TimbreFiscalDigital";

    public static string? Desde(string? xmlTimbrado)
    {
        var timbre = Leer(xmlTimbrado)?.Element(Cfdi + "Complemento")?.Element(Tfd + "TimbreFiscalDigital");
        if (timbre is null) return null;
        string[] obligatorios = ["Version", "UUID", "FechaTimbrado", "RfcProvCertif", "SelloCFD", "NoCertificadoSAT"];
        if (obligatorios.Any(a => string.IsNullOrWhiteSpace((string?)timbre.Attribute(a)))) return null;
        var valores = obligatorios.Take(4).Select(a => (string)timbre.Attribute(a)!).ToList();
        if (timbre.Attribute("Leyenda") is { } leyenda && !string.IsNullOrWhiteSpace(leyenda.Value)) valores.Add(leyenda.Value);
        valores.Add((string)timbre.Attribute("SelloCFD")!);
        valores.Add((string)timbre.Attribute("NoCertificadoSAT")!);
        return "||" + string.Join('|', valores) + "||";
    }

    public static string? UuidDesde(string? xmlTimbrado) =>
        (string?)Leer(xmlTimbrado)?.Element(Cfdi + "Complemento")?.Element(Tfd + "TimbreFiscalDigital")?.Attribute("UUID");

    public static string? CertificadoEmisorDesde(string? xmlTimbrado)
    {
        var valor = (string?)Leer(xmlTimbrado)?.Attribute("NoCertificado");
        return valor?.Length == 20 ? valor : null;
    }

    private static XElement? Leer(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        try
        {
            using var input = new StringReader(xml.TrimStart('\uFEFF'));
            using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var root = XDocument.Load(reader).Root;
            return root?.Name == Cfdi + "Comprobante" ? root : null;
        }
        catch (XmlException)
        {
            // Un resultado fiscal confirmado no se revierte por un XML incompleto del proveedor.
            // El mantenimiento permite completar los datos al recuperar el archivo correcto.
            return null;
        }
    }
}
