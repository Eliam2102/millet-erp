using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Facturas;

namespace Millet.Facturacion.Application.Proformas;

/// <summary>
/// Traduce una <see cref="FacturaVenta"/> (armada, no necesariamente
/// persistida) al contenido comparable de la proforma (U1.8a). Reutiliza
/// <see cref="CfdiEmisionBuilder.DesdeFacturaVenta"/> — el mismo armado que se
/// envía al PAC — para emisor, receptor, conceptos e impuestos; los totales se
/// toman del agregado.
/// </summary>
public static class ProformaCfdiMapper
{
    private const string ImpuestoIva = "002";
    private const string ImpuestoIsr = "001";

    public static ProformaCfdi Desde(FacturaVenta factura, DateTimeOffset ahoraUtc, string serieFiscalPrevista)
    {
        ArgumentNullException.ThrowIfNull(factura);
        var emision = CfdiEmisionBuilder.DesdeFacturaVenta(factura, ahoraUtc);

        var conceptos = emision.Conceptos
            .Select((c, i) =>
            {
                var trasladoIva = c.Impuestos.FirstOrDefault(x => !x.EsRetencion && x.Impuesto == ImpuestoIva);
                return new ProformaConcepto(
                    Posicion: i + 1,
                    ClaveProdServ: c.ClaveProdServ,
                    ClaveUnidad: c.ClaveUnidad,
                    Descripcion: c.Descripcion,
                    Cantidad: c.Cantidad,
                    ValorUnitario: c.ValorUnitario,
                    Importe: c.Importe,
                    Descuento: c.Descuento,
                    ObjetoImp: c.ObjetoImp,
                    Iva: c.Impuestos.Where(x => !x.EsRetencion).Sum(x => x.Importe),
                    RetencionIva: c.Impuestos.Where(x => x.EsRetencion && x.Impuesto == ImpuestoIva).Sum(x => x.Importe),
                    RetencionIsr: c.Impuestos.Where(x => x.EsRetencion && x.Impuesto == ImpuestoIsr).Sum(x => x.Importe),
                    TasaIva: trasladoIva is { TipoFactor: "Tasa" } ? trasladoIva.TasaOCuota : null);
            })
            .ToList();

        return new ProformaCfdi(
            Emisor: new ProformaEmisor(emision.Emisor.Rfc, emision.Emisor.Nombre, emision.Emisor.RegimenFiscal, emision.Emisor.LugarExpedicion),
            Receptor: new ProformaReceptor(emision.Receptor.Rfc, emision.Receptor.Nombre, emision.Receptor.RegimenFiscal,
                emision.Receptor.CodigoPostal, emision.Receptor.UsoCfdi),
            MetodoPago: emision.MetodoPago,
            FormaPago: emision.FormaPago,
            Moneda: emision.Moneda,
            TipoCambio: emision.TipoCambio,
            Exportacion: emision.Exportacion,
            SerieFiscalPrevista: serieFiscalPrevista,
            Conceptos: conceptos,
            Totales: new ProformaTotales(factura.Subtotal, factura.Descuento, factura.ImpuestosTrasladados,
                factura.Retenciones, factura.Total));
    }
}
