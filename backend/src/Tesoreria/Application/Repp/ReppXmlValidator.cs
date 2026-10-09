using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.Tesoreria.Application.Repp;
public sealed record PagoFiscalRepp(DateOnly Fecha, string Moneda, decimal Importe);
public static class ReppXmlValidator
{
    public static IReadOnlyList<PagoFiscalRepp> Validar(byte[] xml, Guid uuid, DateOnly fecha, string rfcProveedor, Guid facturaUuid, string moneda)
    {
        try
        {
            using var stream = new MemoryStream(xml);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 5_000_000 });
            var doc = XDocument.Load(reader);
            XNamespace cfdi = "http://www.sat.gob.mx/cfd/4", pagos = "http://www.sat.gob.mx/Pagos20", timbre = "http://www.sat.gob.mx/TimbreFiscalDigital";
            var root = doc.Root;
            if (root?.Name != cfdi + "Comprobante" || (string?)root.Attribute("TipoDeComprobante") != "P" || (string?)root.Attribute("Version") != "4.0") Fallar("El XML debe ser un CFDI 4.0 de tipo Pago con complemento Pagos 2.0.");
            if (!string.Equals((string?)root!.Element(cfdi + "Emisor")?.Attribute("Rfc"), rfcProveedor, StringComparison.OrdinalIgnoreCase)) Fallar("El RFC emisor del REPP no corresponde al proveedor de la factura.");
            var t = root.Element(cfdi + "Complemento")?.Element(timbre + "TimbreFiscalDigital");
            if (!Guid.TryParse((string?)t?.Attribute("UUID"), out var fiscalUuid) || fiscalUuid != uuid) Fallar("El UUID del timbre no coincide con el complemento registrado.");
            if (!DateTime.TryParse((string?)root.Attribute("Fecha"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var fechaCfdi) || DateOnly.FromDateTime(fechaCfdi) != fecha) Fallar("La fecha registrada no coincide con la fecha del CFDI del complemento.");
            var complemento = root.Element(cfdi + "Complemento")?.Element(pagos + "Pagos");
            if ((string?)complemento?.Attribute("Version") != "2.0") Fallar("Falta el complemento Pagos 2.0.");
            var resultado = new List<PagoFiscalRepp>();
            foreach (var pago in complemento!.Elements(pagos + "Pago"))
            {
                if (!DateTime.TryParse((string?)pago.Attribute("FechaPago"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var fechaPago)) Fallar("El complemento contiene una fecha de pago inválida.");
                var documentos = pago.Elements(pagos + "DoctoRelacionado").ToList();
                decimal suma = 0;
                foreach (var d in documentos)
                {
                    if (!Guid.TryParse((string?)d.Attribute("IdDocumento"), out var relacionado) || relacionado != facturaUuid) Fallar("El complemento relaciona una factura distinta. Registra el REPP contra la factura que corresponde al XML.");
                    if ((string?)d.Attribute("MonedaDR") != moneda || (string?)pago.Attribute("MonedaP") != moneda) Fallar("La moneda del complemento debe coincidir con la factura y el pago.");
                    if (!decimal.TryParse((string?)d.Attribute("ImpPagado"), NumberStyles.Number, CultureInfo.InvariantCulture, out var importe) || importe <= 0) Fallar("El importe pagado del XML debe ser mayor a cero.");
                    if (!int.TryParse((string?)d.Attribute("NumParcialidad"), out var parcialidad) || parcialidad <= 0) Fallar("La parcialidad del XML debe ser un entero positivo.");
                    if (!decimal.TryParse((string?)d.Attribute("ImpSaldoAnt"), NumberStyles.Number, CultureInfo.InvariantCulture, out var anterior) ||
                        !decimal.TryParse((string?)d.Attribute("ImpSaldoInsoluto"), NumberStyles.Number, CultureInfo.InvariantCulture, out var insoluto) || anterior < importe || insoluto < 0 || Math.Abs(anterior - importe - insoluto) > 0.01m)
                        Fallar("Los saldos de la parcialidad no coinciden con su importe pagado.");
                    suma += importe;
                }
                if (suma <= 0 || !decimal.TryParse((string?)pago.Attribute("Monto"), NumberStyles.Number, CultureInfo.InvariantCulture, out var monto) || Math.Abs(monto - suma) > 0.01m) Fallar("El monto del pago en el XML no coincide con los documentos relacionados.");
                resultado.Add(new(DateOnly.FromDateTime(fechaPago), moneda, suma));
            }
            if (resultado.Count == 0) Fallar("El XML no contiene pagos de la factura.");
            return resultado;
        }
        catch (XmlException) { throw new BusinessRuleException("REPP_XML_INVALIDO", "El XML del complemento es inválido. Verifica el archivo del proveedor."); }
    }
    private static void Fallar(string mensaje) => throw new BusinessRuleException("REPP_XML_NO_COINCIDE", mensaje);
}
