using System.Globalization;

namespace Millet.Facturacion.Application.Proformas;

/// <summary>
/// Compara la última proforma entregada contra lo que se va a timbrar (U1.8a
/// CA-c). Devuelve una diferencia por campo con etiquetas legibles en español
/// ("Receptor · RFC", "Concepto 2 · Cantidad", "Total") y los valores ya
/// formateados. Los conceptos se comparan por posición; uno que sólo existe de
/// un lado se reporta como alta/baja ("Concepto n").
/// La serie fiscal prevista no se compara: el folio fiscal se asigna al timbrar.
/// </summary>
public static class ProformaComparador
{
    public static IReadOnlyList<DiferenciaProforma> Comparar(ProformaCfdi proforma, ProformaCfdi timbrado)
    {
        ArgumentNullException.ThrowIfNull(proforma);
        ArgumentNullException.ThrowIfNull(timbrado);
        var dif = new List<DiferenciaProforma>();

        Texto(dif, "Emisor · RFC", proforma.Emisor.Rfc, timbrado.Emisor.Rfc);
        Texto(dif, "Emisor · Nombre", proforma.Emisor.Nombre, timbrado.Emisor.Nombre);
        Texto(dif, "Emisor · Régimen fiscal", proforma.Emisor.RegimenFiscal, timbrado.Emisor.RegimenFiscal);
        Texto(dif, "Emisor · Lugar de expedición", proforma.Emisor.LugarExpedicion, timbrado.Emisor.LugarExpedicion);

        Texto(dif, "Receptor · RFC", proforma.Receptor.Rfc, timbrado.Receptor.Rfc);
        Texto(dif, "Receptor · Nombre", proforma.Receptor.Nombre, timbrado.Receptor.Nombre);
        Texto(dif, "Receptor · Régimen fiscal", proforma.Receptor.RegimenFiscal, timbrado.Receptor.RegimenFiscal);
        Texto(dif, "Receptor · Código postal", proforma.Receptor.CodigoPostal, timbrado.Receptor.CodigoPostal);
        Texto(dif, "Receptor · Uso CFDI", proforma.Receptor.UsoCfdi, timbrado.Receptor.UsoCfdi);

        Texto(dif, "Método de pago", proforma.MetodoPago, timbrado.MetodoPago);
        Texto(dif, "Forma de pago", proforma.FormaPago, timbrado.FormaPago);
        Texto(dif, "Moneda", proforma.Moneda, timbrado.Moneda);
        // En moneda extranjera la proforma no fija el tipo de cambio (se fija al
        // timbrar): con la misma moneda, un TC que la proforma no traía no es diferencia.
        if (proforma.TipoCambio is not null || proforma.Moneda != timbrado.Moneda)
            Numero(dif, "Tipo de cambio", proforma.TipoCambio, timbrado.TipoCambio);
        Texto(dif, "Exportación", proforma.Exportacion, timbrado.Exportacion);

        var maxPos = Math.Max(proforma.Conceptos.Count, timbrado.Conceptos.Count);
        for (var i = 0; i < maxPos; i++)
        {
            var a = i < proforma.Conceptos.Count ? proforma.Conceptos[i] : null;
            var b = i < timbrado.Conceptos.Count ? timbrado.Conceptos[i] : null;
            var etiqueta = string.Create(CultureInfo.InvariantCulture, $"Concepto {i + 1}");
            if (a is null || b is null)
            {
                dif.Add(new DiferenciaProforma(etiqueta, Resumen(a), Resumen(b)));
                continue;
            }

            Texto(dif, $"{etiqueta} · Clave producto/servicio", a.ClaveProdServ, b.ClaveProdServ);
            Texto(dif, $"{etiqueta} · Clave unidad", a.ClaveUnidad, b.ClaveUnidad);
            Texto(dif, $"{etiqueta} · Descripción", a.Descripcion, b.Descripcion);
            Numero(dif, $"{etiqueta} · Cantidad", a.Cantidad, b.Cantidad);
            Importe(dif, $"{etiqueta} · Valor unitario", a.ValorUnitario, b.ValorUnitario);
            Importe(dif, $"{etiqueta} · Importe", a.Importe, b.Importe);
            Importe(dif, $"{etiqueta} · Descuento", a.Descuento, b.Descuento);
            Texto(dif, $"{etiqueta} · Objeto de impuesto", a.ObjetoImp, b.ObjetoImp);
            Numero(dif, $"{etiqueta} · Tasa IVA", a.TasaIva, b.TasaIva);
            Importe(dif, $"{etiqueta} · IVA", a.Iva, b.Iva);
            Importe(dif, $"{etiqueta} · Retención IVA", a.RetencionIva, b.RetencionIva);
            Importe(dif, $"{etiqueta} · Retención ISR", a.RetencionIsr, b.RetencionIsr);
        }

        Importe(dif, "Subtotal", proforma.Totales.Subtotal, timbrado.Totales.Subtotal);
        Importe(dif, "Descuento", proforma.Totales.Descuento, timbrado.Totales.Descuento);
        Importe(dif, "IVA trasladado", proforma.Totales.ImpuestosTrasladados, timbrado.Totales.ImpuestosTrasladados);
        Importe(dif, "Retenciones", proforma.Totales.Retenciones, timbrado.Totales.Retenciones);
        Importe(dif, "Total", proforma.Totales.Total, timbrado.Totales.Total);

        return dif;
    }

    private static void Texto(List<DiferenciaProforma> dif, string campo, string? a, string? b)
    {
        var na = Normalizar(a);
        var nb = Normalizar(b);
        if (!string.Equals(na, nb, StringComparison.OrdinalIgnoreCase))
            dif.Add(new DiferenciaProforma(campo, na, nb));
    }

    private static void Numero(List<DiferenciaProforma> dif, string campo, decimal? a, decimal? b)
    {
        if (a != b)
            dif.Add(new DiferenciaProforma(campo, FormatoNumero(a), FormatoNumero(b)));
    }

    private static void Importe(List<DiferenciaProforma> dif, string campo, decimal a, decimal b)
    {
        if (a != b)
            dif.Add(new DiferenciaProforma(campo, FormatoImporte(a), FormatoImporte(b)));
    }

    private static string? Normalizar(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static string? FormatoNumero(decimal? v) =>
        v?.ToString("0.######", CultureInfo.InvariantCulture);

    private static string FormatoImporte(decimal v) =>
        v.ToString("0.00", CultureInfo.InvariantCulture);

    private static string? Resumen(ProformaConcepto? c) =>
        c is null
            ? null
            : string.Create(CultureInfo.InvariantCulture,
                $"{c.Descripcion} · {FormatoNumero(c.Cantidad)} × {FormatoImporte(c.ValorUnitario)} = {FormatoImporte(c.Importe)}");
}
