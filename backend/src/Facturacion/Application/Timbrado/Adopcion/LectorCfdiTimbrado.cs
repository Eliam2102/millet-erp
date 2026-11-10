using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Timbrado.Adopcion;

/// <summary>Datos del CFDI 4.0 timbrado que se cotejan contra el comprobante del ERP (M-284).</summary>
public sealed record CfdiTimbradoLeido(
    string? Serie,
    string? Folio,
    decimal Total,
    string TipoDeComprobante,
    string RfcEmisor,
    string RfcReceptor,
    string Uuid,
    DateTime FechaTimbradoLocal,
    string? SelloCfd,
    string? SelloSat,
    string? NoCertificadoSat,
    string? RfcProvCertif,
    string? SelloComprobante = null);

/// <summary>
/// Lectura del XML de un CFDI 4.0 timbrado que el usuario descarga del portal del PAC (M-284).
/// Archivo de fuera: sin DTD ni resolución de entidades externas y con tope de tamaño.
/// </summary>
public static class LectorCfdiTimbrado
{
    /// <summary>Tope del XML cargado (un CFDI con complemento ronda decenas de KB).</summary>
    public const int TamanoMaximo = 2 * 1024 * 1024;

    private static readonly XNamespace Cfdi = "http://www.sat.gob.mx/cfd/4";
    private static readonly XNamespace Tfd = "http://www.sat.gob.mx/TimbreFiscalDigital";

    public static CfdiTimbradoLeido Leer(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw Invalido("Carga el XML del CFDI timbrado.");
        if (xml.Length > TamanoMaximo)
            throw Invalido("El XML excede 2 MB; no parece un CFDI.");

        XElement raiz;
        try
        {
            using var input = new StringReader(xml.TrimStart('﻿'));
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = TamanoMaximo,
            });
            raiz = XDocument.Load(reader).Root ?? throw Invalido("El archivo no es un CFDI.");
        }
        catch (XmlException)
        {
            throw Invalido("El archivo no es un XML válido.");
        }

        if (raiz.Name != Cfdi + "Comprobante" || (string?)raiz.Attribute("Version") != "4.0")
            throw Invalido("El archivo no es un CFDI 4.0.");
        var timbre = raiz.Element(Cfdi + "Complemento")?.Element(Tfd + "TimbreFiscalDigital");
        var uuid = (string?)timbre?.Attribute("UUID");
        if (timbre is null || string.IsNullOrWhiteSpace(uuid))
            throw Invalido("El XML no trae el Timbre Fiscal Digital (UUID); no es un CFDI timbrado.");
        if (!DateTime.TryParse((string?)timbre.Attribute("FechaTimbrado"), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var fecha))
            throw Invalido("El Timbre Fiscal Digital no trae una fecha de timbrado válida.");
        if (!decimal.TryParse((string?)raiz.Attribute("Total"), NumberStyles.Number, CultureInfo.InvariantCulture, out var total))
            throw Invalido("El XML no trae un total válido.");

        return new CfdiTimbradoLeido(
            Serie: (string?)raiz.Attribute("Serie"),
            Folio: (string?)raiz.Attribute("Folio"),
            Total: total,
            TipoDeComprobante: (string?)raiz.Attribute("TipoDeComprobante") ?? string.Empty,
            RfcEmisor: (string?)raiz.Element(Cfdi + "Emisor")?.Attribute("Rfc") ?? string.Empty,
            RfcReceptor: (string?)raiz.Element(Cfdi + "Receptor")?.Attribute("Rfc") ?? string.Empty,
            Uuid: uuid.Trim(),
            FechaTimbradoLocal: fecha,
            SelloCfd: (string?)timbre.Attribute("SelloCFD"),
            SelloSat: (string?)timbre.Attribute("SelloSAT"),
            NoCertificadoSat: (string?)timbre.Attribute("NoCertificadoSAT"),
            RfcProvCertif: (string?)timbre.Attribute("RfcProvCertif"),
            SelloComprobante: (string?)raiz.Attribute("Sello"));
    }

    private static BusinessRuleException Invalido(string mensaje) => new("CFDI_XML_INVALIDO", mensaje);
}
