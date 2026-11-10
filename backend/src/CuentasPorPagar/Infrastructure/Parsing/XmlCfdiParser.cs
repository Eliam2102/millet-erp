using System.Globalization;
using System.Xml.Linq;
using Millet.CuentasPorPagar.Domain.Cfdi;

namespace Millet.CuentasPorPagar.Infrastructure.Parsing;

/// <summary>
/// Implementación del <see cref="IXmlCfdiParser"/> con LINQ to XML
/// (F1-PR1). Cubre la cabecera de CFDI 4.0 + impuestos totales +
/// líneas de <c>cfdi:Concepto</c>.
///
/// <para>
/// Namespaces fijos:
/// <list type="bullet">
///   <item><c>cfdi</c> → <c>http://www.sat.gob.mx/cfd/4</c></item>
///   <item><c>tfd</c>  → <c>http://www.sat.gob.mx/TimbreFiscalDigital</c></item>
/// </list>
/// Sin sello-verification ni validación XSD — esos los hace el SAT.
/// El parser confía en que el XML que llega ya fue timbrado.
/// </para>
/// </summary>
public sealed class XmlCfdiParser : IXmlCfdiParser
{
    private static readonly XNamespace NsCfdi = "http://www.sat.gob.mx/cfd/4";
    private static readonly XNamespace NsTfd  = "http://www.sat.gob.mx/TimbreFiscalDigital";

    public DatosCfdiParseados Parsear(Stream xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(xml);
        }
        catch (Exception ex)
        {
            throw new CfdiParseException("CFDI_XML_MALFORMADO", $"El XML no se pudo parsear: {ex.Message}");
        }

        return ParsearDocument(doc);
    }

    public DatosCfdiParseados Parsear(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (Exception ex)
        {
            throw new CfdiParseException("CFDI_XML_MALFORMADO", $"El XML no se pudo parsear: {ex.Message}");
        }

        return ParsearDocument(doc);
    }

    private static DatosCfdiParseados ParsearDocument(XDocument doc)
    {
        var comprobante = doc.Root
            ?? throw new CfdiParseException("CFDI_SIN_ROOT", "El XML no tiene elemento raíz.");

        if (comprobante.Name.LocalName != "Comprobante" || comprobante.Name.Namespace != NsCfdi)
        {
            throw new CfdiParseException(
                "CFDI_ROOT_INVALIDO",
                $"El elemento raíz esperado es {{cfdi:Comprobante}} (versión 4.0); recibido '{comprobante.Name}'.");
        }

        var version = AttrOrEmpty(comprobante, "Version");
        if (version != "4.0")
        {
            throw new CfdiParseException(
                "CFDI_VERSION_NO_SOPORTADA",
                $"Solo se soporta CFDI 4.0; el XML reporta versión '{version}'.");
        }

        var timbre = comprobante
            .Element(NsCfdi + "Complemento")
            ?.Element(NsTfd + "TimbreFiscalDigital")
            ?? throw new CfdiParseException(
                "CFDI_SIN_TIMBRE",
                "El CFDI no tiene complemento TimbreFiscalDigital; no es un CFDI timbrado válido.");

        var uuid = AttrOrEmpty(timbre, "UUID");
        if (string.IsNullOrWhiteSpace(uuid))
        {
            throw new CfdiParseException("CFDI_UUID_VACIO", "El TimbreFiscalDigital no tiene UUID.");
        }

        var emisor = comprobante.Element(NsCfdi + "Emisor")
            ?? throw new CfdiParseException("CFDI_SIN_EMISOR", "El CFDI no tiene elemento Emisor.");
        var receptor = comprobante.Element(NsCfdi + "Receptor")
            ?? throw new CfdiParseException("CFDI_SIN_RECEPTOR", "El CFDI no tiene elemento Receptor.");

        var tipo = ParseTipo(AttrOrEmpty(comprobante, "TipoDeComprobante"));
        var fecha = ParseFechaCfdi(AttrOrEmpty(comprobante, "Fecha"));

        var moneda = AttrOrEmpty(comprobante, "Moneda");
        if (string.IsNullOrWhiteSpace(moneda)) moneda = "MXN";

        var tipoCambio = ParseDecimalOrNull(AttrOrNull(comprobante, "TipoCambio"));

        var total = ParseDecimal(AttrOrEmpty(comprobante, "Total"));
        var subtotal = ParseDecimal(AttrOrEmpty(comprobante, "SubTotal"));

        var (impuestos, retenciones) = ParseImpuestos(comprobante);
        var lineas = ParseLineas(comprobante);
        var relacionados = ParseCfdiRelacionados(comprobante);

        return new DatosCfdiParseados(
            UuidCfdi: uuid,
            RfcEmisor: AttrOrEmpty(emisor, "Rfc"),
            RazonSocialEmisor: AttrOrEmpty(emisor, "Nombre"),
            RfcReceptor: AttrOrEmpty(receptor, "Rfc"),
            RazonSocialReceptor: AttrOrEmpty(receptor, "Nombre"),
            Tipo: tipo,
            Folio: AttrOrNull(comprobante, "Folio"),
            Serie: AttrOrNull(comprobante, "Serie"),
            FechaCfdi: fecha,
            Total: total,
            Subtotal: subtotal,
            ImpuestosTrasladados: impuestos,
            Retenciones: retenciones,
            Moneda: moneda,
            TipoCambio: tipoCambio,
            Lineas: lineas,
            MetodoPago: AttrOrNull(comprobante, "MetodoPago"),
            CfdiRelacionados: relacionados,
            RetencionesDetalle: ParseRetenciones(comprobante),
            TrasladosIva: ParseTrasladosIva(comprobante),
            Descuentos: ParseDecimalOrNull(AttrOrNull(comprobante, "Descuento")) ?? 0m);
    }

    /// <summary>
    /// M-209: IVA trasladado por tratamiento, sumado de los conceptos. Cada concepto aporta su traslado 002 (base e importe por tasa, o
    /// solo la base si es Exento); un concepto con <c>ObjetoImp</c> 01 aporta su importe neto de descuento como NOOBJETO. Null si el CFDI
    /// no trae conceptos o ninguno se puede clasificar (p. ej. CFDI 3.3 sin traslados por concepto): Contabilidad lo trata como hoy.
    /// </summary>
    private static List<TrasladoIvaCfdi>? ParseTrasladosIva(XElement comprobante)
    {
        var partes = new List<(string Tratamiento, decimal Base, decimal Iva)>();
        foreach (var concepto in comprobante.Element(NsCfdi + "Conceptos")?.Elements(NsCfdi + "Concepto") ?? [])
        {
            var objetoImp = AttrOrNull(concepto, "ObjetoImp");
            if (string.Equals(objetoImp?.Trim(), "01", StringComparison.Ordinal))
            {
                var neto = ParseDecimal(AttrOrEmpty(concepto, "Importe")) - (ParseDecimalOrNull(AttrOrNull(concepto, "Descuento")) ?? 0m);
                partes.Add((TrasladoIvaCfdi.NoObjeto, neto, 0m));
                continue;
            }
            var traslados = concepto.Element(NsCfdi + "Impuestos")?.Element(NsCfdi + "Traslados")?.Elements(NsCfdi + "Traslado")
                .Where(t => AttrOrEmpty(t, "Impuesto").Trim() is "002" or "IVA") ?? [];
            foreach (var t in traslados)
            {
                var tratamiento = TrasladoIvaCfdi.De(objetoImp, AttrOrNull(t, "TipoFactor"), ParseDecimalOrNull(AttrOrNull(t, "TasaOCuota")));
                if (tratamiento is null) continue;
                partes.Add((tratamiento, ParseDecimal(AttrOrEmpty(t, "Base")), ParseDecimalOrNull(AttrOrNull(t, "Importe")) ?? 0m));
            }
        }
        if (partes.Count == 0) return null;
        return [.. partes.GroupBy(p => p.Tratamiento).Select(g => new TrasladoIvaCfdi(g.Key, g.Sum(p => p.Base), g.Sum(p => p.Iva)))];
    }

    private static List<RetencionCfdi>? ParseRetenciones(XElement comprobante)
    {
        var nodos = comprobante.Element(NsCfdi + "Impuestos")?
            .Element(NsCfdi + "Retenciones")?
            .Elements(NsCfdi + "Retencion")
            .Select(r => new RetencionCfdi(
                AttrOrEmpty(r, "Impuesto").Trim(),
                Tasa: null,
                ParseDecimal(AttrOrEmpty(r, "Importe"))))
            .ToList();
        if (nodos is not { Count: > 0 }) return null;

        // 86e3zcbjw: la retención es por línea. Las retenciones de los conceptos dan la tasa y la base sobre la que se retuvo (puede ser
        // solo parte de las líneas). Con una sola tasa por impuesto se conserva el importe del comprobante (el que cuadra con el total);
        // con varias, una entrada por tasa con lo de sus conceptos.
        var porConcepto = comprobante.Element(NsCfdi + "Conceptos")?.Elements(NsCfdi + "Concepto")
            .SelectMany(c => c.Element(NsCfdi + "Impuestos")?.Element(NsCfdi + "Retenciones")?.Elements(NsCfdi + "Retencion") ?? [])
            .Select(r => (Impuesto: AttrOrEmpty(r, "Impuesto").Trim(), Tasa: ParseDecimalOrNull(AttrOrNull(r, "TasaOCuota")),
                Base: ParseDecimal(AttrOrEmpty(r, "Base")), Importe: ParseDecimal(AttrOrEmpty(r, "Importe"))))
            .ToList() ?? [];
        if (porConcepto.Count == 0) return nodos;
        var resultado = new List<RetencionCfdi>();
        foreach (var nodo in nodos)
        {
            var tasas = porConcepto.Where(r => r.Impuesto == nodo.Impuesto).GroupBy(r => r.Tasa).ToList();
            if (tasas.Count == 0) resultado.Add(nodo);
            else if (tasas.Count == 1) resultado.Add(nodo with { Tasa = tasas[0].Key, Base = tasas[0].Sum(r => r.Base) });
            else resultado.AddRange(tasas.Select(g => new RetencionCfdi(nodo.Impuesto, g.Key, g.Sum(r => r.Importe), g.Sum(r => r.Base))));
        }
        return resultado;
    }

    private static List<CfdiRelacionadosParseados>? ParseCfdiRelacionados(XElement comprobante)
    {
        // CFDI 4.0 admite múltiples nodos <cfdi:CfdiRelacionados>, cada uno
        // con su TipoRelacion y N hijos <cfdi:CfdiRelacionado UUID="...">.
        List<CfdiRelacionadosParseados>? resultado = null;
        foreach (var nodo in comprobante.Elements(NsCfdi + "CfdiRelacionados"))
        {
            var uuids = nodo.Elements(NsCfdi + "CfdiRelacionado")
                .Select(r => AttrOrEmpty(r, "UUID").Trim().ToUpperInvariant())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .ToList();
            if (uuids.Count == 0) continue;

            resultado ??= [];
            resultado.Add(new CfdiRelacionadosParseados(
                TipoRelacion: AttrOrEmpty(nodo, "TipoRelacion").Trim(),
                Uuids: uuids));
        }
        return resultado;
    }

    private static (decimal Trasladados, decimal Retenciones) ParseImpuestos(XElement comprobante)
    {
        var impuestos = comprobante.Element(NsCfdi + "Impuestos");
        if (impuestos is null) return (0m, 0m);

        var trasladados = ParseDecimalOrNull(AttrOrNull(impuestos, "TotalImpuestosTrasladados")) ?? 0m;
        var retenidos = ParseDecimalOrNull(AttrOrNull(impuestos, "TotalImpuestosRetenidos")) ?? 0m;
        return (trasladados, retenidos);
    }

    private static List<LineaCfdiParseada> ParseLineas(XElement comprobante)
    {
        var conceptos = comprobante.Element(NsCfdi + "Conceptos");
        if (conceptos is null) return [];

        var lineas = new List<LineaCfdiParseada>();
        var pos = 1;
        foreach (var concepto in conceptos.Elements(NsCfdi + "Concepto"))
        {
            lineas.Add(new LineaCfdiParseada(
                Posicion: pos++,
                ClaveProdServ: AttrOrEmpty(concepto, "ClaveProdServ"),
                NoIdentificacion: AttrOrNull(concepto, "NoIdentificacion"),
                Cantidad: ParseDecimal(AttrOrEmpty(concepto, "Cantidad")),
                ClaveUnidad: AttrOrEmpty(concepto, "ClaveUnidad"),
                Unidad: AttrOrNull(concepto, "Unidad"),
                Descripcion: AttrOrEmpty(concepto, "Descripcion"),
                ValorUnitario: ParseDecimal(AttrOrEmpty(concepto, "ValorUnitario")),
                Importe: ParseDecimal(AttrOrEmpty(concepto, "Importe")),
                Descuento: ParseDecimalOrNull(AttrOrNull(concepto, "Descuento"))));
        }
        return lineas;
    }

    private static TipoCfdi ParseTipo(string raw) => raw.ToUpperInvariant() switch
    {
        "I" => TipoCfdi.Ingreso,
        "E" => TipoCfdi.Egreso,
        "P" => TipoCfdi.Pago,
        "T" => TipoCfdi.Traslado,
        "N" => TipoCfdi.Nomina,
        _ => TipoCfdi.Desconocido,
    };

    private static DateTimeOffset ParseFechaCfdi(string raw)
    {
        if (DateTimeOffset.TryParseExact(
                raw,
                "yyyy-MM-ddTHH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var fecha))
        {
            return fecha.ToUniversalTime();
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fallback))
        {
            return fallback.ToUniversalTime();
        }

        throw new CfdiParseException(
            "CFDI_FECHA_INVALIDA",
            $"La fecha '{raw}' no cumple el formato CFDI 4.0 (yyyy-MM-ddTHH:mm:ss).");
    }

    private static decimal ParseDecimal(string raw)
    {
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v))
        {
            return v;
        }
        throw new CfdiParseException("CFDI_NUMERO_INVALIDO", $"No se pudo interpretar como número: '{raw}'.");
    }

    private static decimal? ParseDecimalOrNull(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static string AttrOrEmpty(XElement el, string nombre) => el.Attribute(nombre)?.Value ?? string.Empty;
    private static string? AttrOrNull(XElement el, string nombre) => el.Attribute(nombre)?.Value;
}
