using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Millet.CuentasPorPagar.Infrastructure.GeneradorPrueba;

/// <summary>
/// Generador de XML CFDI 4.0 sintético para pruebas y demostraciones en ambientes no productivos.
/// Produce un comprobante XML válido que parsea limpiamente con <see cref="Parsing.XmlCfdiParser"/>.
/// </summary>
public sealed class GeneradorCfdiPruebaService : IGeneradorCfdiPruebaService
{
    private static readonly XNamespace NsCfdi = "http://www.sat.gob.mx/cfd/4";
    private static readonly XNamespace NsTfd = "http://www.sat.gob.mx/TimbreFiscalDigital";
    private static readonly TimeZoneInfo MexicoCityTimeZone = ObtenerZonaHorariaMexico();

    public string GenerarXml(DatosGeneracionCfdiPrueba datos)
    {
        var doc = ConstruirDocumento(datos);
        var sb = new StringBuilder();
        using (var writer = new Utf8StringWriter(sb))
        using (var xmlWriter = XmlWriter.Create(writer, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            OmitXmlDeclaration = false
        }))
        {
            doc.Save(xmlWriter);
        }
        return sb.ToString();
    }

    public Stream GenerarXmlStream(DatosGeneracionCfdiPrueba datos)
    {
        var doc = ConstruirDocumento(datos);
        var ms = new MemoryStream();
        using (var xmlWriter = XmlWriter.Create(ms, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            OmitXmlDeclaration = false,
            CloseOutput = false
        }))
        {
            doc.Save(xmlWriter);
        }
        ms.Position = 0;
        return ms;
    }

    private static XDocument ConstruirDocumento(DatosGeneracionCfdiPrueba datos)
    {
        ArgumentNullException.ThrowIfNull(datos);

        if (datos.Emisor is null || string.IsNullOrWhiteSpace(datos.Emisor.Rfc))
        {
            throw new ArgumentException("El RFC del emisor (proveedor) es obligatorio.", nameof(datos));
        }

        if (datos.Receptor is null || string.IsNullOrWhiteSpace(datos.Receptor.Rfc))
        {
            throw new ArgumentException("El RFC del receptor es obligatorio.", nameof(datos));
        }

        if (datos.Lineas is null || datos.Lineas.Count == 0 || datos.Lineas.Sum(l => l.Cantidad) <= 0m)
        {
            throw new ArgumentException("Las líneas del comprobante deben sumar una cantidad mayor a cero.", nameof(datos));
        }

        if (datos.Lineas.Any(l => l.Cantidad < 0m))
        {
            throw new ArgumentException("No se permiten cantidades negativas en las líneas.", nameof(datos));
        }

        var lineasValidas = datos.Lineas.Where(l => l.Cantidad > 0m).ToList();
        if (lineasValidas.Count == 0)
        {
            throw new ArgumentException("Ninguna línea tiene cantidad positiva a facturar.", nameof(datos));
        }

        // Cálculos por partida con redondeo exacto al centavo (AwayFromZero)
        var calculosLineas = lineasValidas.Select(l =>
        {
            var importe = decimal.Round(l.Cantidad * l.PrecioUnitario, 2, MidpointRounding.AwayFromZero);
            var descuento = decimal.Round(Math.Max(0m, Math.Min(importe, l.Descuento)), 2, MidpointRounding.AwayFromZero);
            var baseGravable = decimal.Round(importe - descuento, 2, MidpointRounding.AwayFromZero);
            var iva = decimal.Round(baseGravable * l.TasaIva, 2, MidpointRounding.AwayFromZero);
            var retencionIsr = decimal.Round(Math.Max(0m, l.RetencionIsr), 2, MidpointRounding.AwayFromZero);
            var tasaRetencion = l.TasaRetencionIsr ?? (retencionIsr > 0m && baseGravable > 0m
                ? decimal.Round(retencionIsr / baseGravable, 6, MidpointRounding.AwayFromZero)
                : 0m);

            return new
            {
                Linea = l,
                Importe = importe,
                Descuento = descuento,
                BaseGravable = baseGravable,
                Iva = iva,
                RetencionIsr = retencionIsr,
                TasaRetencion = tasaRetencion
            };
        }).ToList();

        var subtotal = decimal.Round(calculosLineas.Sum(x => x.Importe), 2, MidpointRounding.AwayFromZero);
        var totalDescuento = decimal.Round(calculosLineas.Sum(x => x.Descuento), 2, MidpointRounding.AwayFromZero);
        var totalIva = decimal.Round(calculosLineas.Sum(x => x.Iva), 2, MidpointRounding.AwayFromZero);
        var totalRetenciones = decimal.Round(calculosLineas.Sum(x => x.RetencionIsr), 2, MidpointRounding.AwayFromZero);
        var total = decimal.Round(subtotal - totalDescuento + totalIva - totalRetenciones, 2, MidpointRounding.AwayFromZero);

        var uuidStr = (datos.Uuid ?? Guid.NewGuid()).ToString().ToUpperInvariant();
        var folioStr = !string.IsNullOrWhiteSpace(datos.Folio) ? datos.Folio.Trim() : uuidStr[..8];
        var serieStr = !string.IsNullOrWhiteSpace(datos.Serie) ? datos.Serie.Trim() : "PRUEBA";

        var fechaLocal = TimeZoneInfo.ConvertTime(datos.Fecha ?? DateTimeOffset.UtcNow, MexicoCityTimeZone);
        var fechaStr = fechaLocal.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

        var moneda = string.IsNullOrWhiteSpace(datos.Moneda) ? "MXN" : datos.Moneda.Trim().ToUpperInvariant();
        decimal? tipoCambio = moneda != "MXN" ? (datos.TipoCambio ?? 1.0m) : null;

        var metodoPago = string.IsNullOrWhiteSpace(datos.MetodoPago) ? "PPD" : datos.MetodoPago.Trim().ToUpperInvariant();
        var formaPago = !string.IsNullOrWhiteSpace(datos.FormaPago)
            ? datos.FormaPago.Trim()
            : (metodoPago == "PUE" ? "03" : "99");

        var cpReceptor = !string.IsNullOrWhiteSpace(datos.Receptor.CodigoPostal) ? datos.Receptor.CodigoPostal.Trim() : "00000";
        var lugarExpedicion = !string.IsNullOrWhiteSpace(datos.LugarExpedicion) ? datos.LugarExpedicion.Trim() : cpReceptor;
        var usoCfdi = !string.IsNullOrWhiteSpace(datos.UsoCfdi)
            ? datos.UsoCfdi.Trim()
            : (!string.IsNullOrWhiteSpace(datos.Receptor.UsoCfdi) ? datos.Receptor.UsoCfdi.Trim() : "G03");

        var tipoDeComprobante = string.IsNullOrWhiteSpace(datos.TipoDeComprobante) ? "I" : datos.TipoDeComprobante.Trim().ToUpperInvariant();
        var exportacion = string.IsNullOrWhiteSpace(datos.Exportacion) ? "01" : datos.Exportacion.Trim();

        // Construir nodo Comprobante
        var comprobante = new XElement(NsCfdi + "Comprobante",
            new XAttribute(XNamespace.Xmlns + "cfdi", NsCfdi.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "tfd", NsTfd.NamespaceName),
            new XAttribute("Version", "4.0"),
            new XAttribute("Serie", serieStr),
            new XAttribute("Folio", folioStr),
            new XAttribute("Fecha", fechaStr),
            new XAttribute("Sello", "SIN-SELLO-PRUEBA"),
            new XAttribute("FormaPago", formaPago),
            new XAttribute("NoCertificado", "00000000000000000000"),
            new XAttribute("Certificado", string.Empty),
            new XAttribute("SubTotal", subtotal.ToString("F2", CultureInfo.InvariantCulture)));

        if (totalDescuento > 0m)
        {
            comprobante.Add(new XAttribute("Descuento", totalDescuento.ToString("F2", CultureInfo.InvariantCulture)));
        }

        comprobante.Add(
            new XAttribute("Moneda", moneda),
            tipoCambio.HasValue ? new XAttribute("TipoCambio", tipoCambio.Value.ToString("0.####", CultureInfo.InvariantCulture)) : null,
            new XAttribute("Total", total.ToString("F2", CultureInfo.InvariantCulture)),
            new XAttribute("TipoDeComprobante", tipoDeComprobante),
            new XAttribute("Exportacion", exportacion),
            new XAttribute("MetodoPago", metodoPago),
            new XAttribute("LugarExpedicion", lugarExpedicion));

        // Emisor
        comprobante.Add(new XElement(NsCfdi + "Emisor",
            new XAttribute("Rfc", datos.Emisor.Rfc.Trim().ToUpperInvariant()),
            new XAttribute("Nombre", datos.Emisor.RazonSocial.Trim()),
            new XAttribute("RegimenFiscal", datos.Emisor.RegimenFiscal ?? "601")));

        // Receptor
        comprobante.Add(new XElement(NsCfdi + "Receptor",
            new XAttribute("Rfc", datos.Receptor.Rfc.Trim().ToUpperInvariant()),
            new XAttribute("Nombre", datos.Receptor.RazonSocial.Trim()),
            new XAttribute("DomicilioFiscalReceptor", cpReceptor),
            new XAttribute("RegimenFiscalReceptor", datos.Receptor.RegimenFiscal ?? "601"),
            new XAttribute("UsoCFDI", usoCfdi)));

        // Conceptos
        var conceptosEl = new XElement(NsCfdi + "Conceptos");
        foreach (var c in calculosLineas)
        {
            var l = c.Linea;
            var descripcion = !string.IsNullOrWhiteSpace(l.ArticuloNombre)
                ? l.ArticuloNombre.Trim()
                : (!string.IsNullOrWhiteSpace(l.DescripcionExtendida) ? l.DescripcionExtendida.Trim() : $"Línea {l.Posicion}");

            var concepto = new XElement(NsCfdi + "Concepto",
                new XAttribute("ClaveProdServ", !string.IsNullOrWhiteSpace(l.ClaveProdServ) ? l.ClaveProdServ.Trim() : "01010101"),
                !string.IsNullOrWhiteSpace(l.ArticuloClave) ? new XAttribute("NoIdentificacion", l.ArticuloClave.Trim()) : null,
                new XAttribute("Cantidad", l.Cantidad.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("ClaveUnidad", !string.IsNullOrWhiteSpace(l.ClaveUnidad) ? l.ClaveUnidad.Trim() : "H87"),
                new XAttribute("Unidad", !string.IsNullOrWhiteSpace(l.UnidadMedida) ? l.UnidadMedida.Trim() : "PZA"),
                new XAttribute("Descripcion", descripcion),
                new XAttribute("ValorUnitario", l.PrecioUnitario.ToString(l.PrecioUnitario == decimal.Truncate(l.PrecioUnitario) ? "F2" : "0.00####", CultureInfo.InvariantCulture)),
                new XAttribute("Importe", c.Importe.ToString("F2", CultureInfo.InvariantCulture)),
                c.Descuento > 0m ? new XAttribute("Descuento", c.Descuento.ToString("F2", CultureInfo.InvariantCulture)) : null,
                new XAttribute("ObjetoImp", "02"));

            var impuestosConcepto = new XElement(NsCfdi + "Impuestos",
                new XElement(NsCfdi + "Traslados",
                    new XElement(NsCfdi + "Traslado",
                        new XAttribute("Base", c.BaseGravable.ToString("F2", CultureInfo.InvariantCulture)),
                        new XAttribute("Impuesto", "002"),
                        new XAttribute("TipoFactor", "Tasa"),
                        new XAttribute("TasaOCuota", l.TasaIva.ToString("0.000000", CultureInfo.InvariantCulture)),
                        new XAttribute("Importe", c.Iva.ToString("F2", CultureInfo.InvariantCulture)))));

            if (c.RetencionIsr > 0m)
            {
                impuestosConcepto.Add(
                    new XElement(NsCfdi + "Retenciones",
                        new XElement(NsCfdi + "Retencion",
                            new XAttribute("Base", c.BaseGravable.ToString("F2", CultureInfo.InvariantCulture)),
                            new XAttribute("Impuesto", "001"),
                            new XAttribute("TipoFactor", "Tasa"),
                            new XAttribute("TasaOCuota", c.TasaRetencion.ToString("0.000000", CultureInfo.InvariantCulture)),
                            new XAttribute("Importe", c.RetencionIsr.ToString("F2", CultureInfo.InvariantCulture)))));
            }

            concepto.Add(impuestosConcepto);
            conceptosEl.Add(concepto);
        }
        comprobante.Add(conceptosEl);

        // Impuestos Globales
        var impuestosGlobales = new XElement(NsCfdi + "Impuestos");
        if (totalRetenciones > 0m)
        {
            impuestosGlobales.Add(new XAttribute("TotalImpuestosRetenidos", totalRetenciones.ToString("F2", CultureInfo.InvariantCulture)));
            impuestosGlobales.Add(new XElement(NsCfdi + "Retenciones",
                new XElement(NsCfdi + "Retencion",
                    new XAttribute("Impuesto", "001"),
                    new XAttribute("Importe", totalRetenciones.ToString("F2", CultureInfo.InvariantCulture)))));
        }

        impuestosGlobales.Add(new XAttribute("TotalImpuestosTrasladados", totalIva.ToString("F2", CultureInfo.InvariantCulture)));

        var trasladosEl = new XElement(NsCfdi + "Traslados");
        var trasladosAgrupados = calculosLineas
            .GroupBy(x => x.Linea.TasaIva)
            .OrderBy(g => g.Key);

        foreach (var grupo in trasladosAgrupados)
        {
            var baseSum = decimal.Round(grupo.Sum(x => x.BaseGravable), 2, MidpointRounding.AwayFromZero);
            var ivaSum = decimal.Round(grupo.Sum(x => x.Iva), 2, MidpointRounding.AwayFromZero);

            trasladosEl.Add(new XElement(NsCfdi + "Traslado",
                new XAttribute("Base", baseSum.ToString("F2", CultureInfo.InvariantCulture)),
                new XAttribute("Impuesto", "002"),
                new XAttribute("TipoFactor", "Tasa"),
                new XAttribute("TasaOCuota", grupo.Key.ToString("0.000000", CultureInfo.InvariantCulture)),
                new XAttribute("Importe", ivaSum.ToString("F2", CultureInfo.InvariantCulture))));
        }
        impuestosGlobales.Add(trasladosEl);
        comprobante.Add(impuestosGlobales);

        // Complemento TimbreFiscalDigital
        comprobante.Add(new XElement(NsCfdi + "Complemento",
            new XElement(NsTfd + "TimbreFiscalDigital",
                new XAttribute("Version", "1.1"),
                new XAttribute("UUID", uuidStr),
                new XAttribute("FechaTimbrado", fechaStr),
                new XAttribute("RfcProvCertif", "SPR190613I52"),
                new XAttribute("SelloCFD", "SIN-SELLO-PRUEBA"),
                new XAttribute("NoCertificadoSAT", "00000000000000000000"),
                new XAttribute("SelloSAT", "SIN-SELLO-PRUEBA"))));

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XComment(" CFDI DE PRUEBA generado sinteticamente para ambiente de pruebas. Sin timbrar: no tiene validez fiscal. "),
            comprobante);

        return doc;
    }

    private static TimeZoneInfo ObtenerZonaHorariaMexico()
    {
        foreach (var id in new[] { "America/Mexico_City", "Central Standard Time (Mexico)", "Central Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }
        return TimeZoneInfo.Local;
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public Utf8StringWriter(StringBuilder sb) : base(sb) { }
        public override Encoding Encoding => Encoding.UTF8;
    }
}
