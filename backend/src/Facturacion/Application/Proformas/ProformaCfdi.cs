namespace Millet.Facturacion.Application.Proformas;

/// <summary>
/// Contenido comparable de una proforma (U1.8a): los mismos datos que llevará
/// el CFDI — emisor, receptor, condiciones de pago, conceptos con impuestos y
/// totales — sin folio fiscal, sello ni UUID. Se arma con
/// <see cref="ProformaCfdiMapper"/> desde el mismo agregado que se timbra, por
/// lo que proforma y factura timbrada son comparables campo a campo.
/// </summary>
/// <param name="SerieFiscalPrevista">
/// Tipo de serie con la que se timbrará (<c>Cfdi</c> o <c>VentaDirecta</c>); el
/// folio fiscal se asigna al timbrar.
/// </param>
public sealed record ProformaCfdi(
    ProformaEmisor Emisor,
    ProformaReceptor Receptor,
    string MetodoPago,
    string FormaPago,
    string Moneda,
    decimal? TipoCambio,
    string Exportacion,
    string SerieFiscalPrevista,
    IReadOnlyList<ProformaConcepto> Conceptos,
    ProformaTotales Totales);

public sealed record ProformaEmisor(string Rfc, string Nombre, string RegimenFiscal, string LugarExpedicion);

public sealed record ProformaReceptor(string Rfc, string Nombre, string RegimenFiscal, string CodigoPostal, string UsoCfdi);

/// <summary>
/// Concepto de la proforma. <paramref name="Iva"/> es el IVA trasladado del
/// concepto; <paramref name="TasaIva"/> es <c>null</c> si es exento o no objeto.
/// </summary>
public sealed record ProformaConcepto(
    int Posicion,
    string ClaveProdServ,
    string ClaveUnidad,
    string Descripcion,
    decimal Cantidad,
    decimal ValorUnitario,
    decimal Importe,
    decimal Descuento,
    string ObjetoImp,
    decimal Iva,
    decimal RetencionIva,
    decimal RetencionIsr,
    decimal? TasaIva);

public sealed record ProformaTotales(
    decimal Subtotal,
    decimal Descuento,
    decimal ImpuestosTrasladados,
    decimal Retenciones,
    decimal Total);

/// <summary>Diferencia entre la última proforma y lo que se va a timbrar (valores ya formateados para mostrar).</summary>
public sealed record DiferenciaProforma(string Campo, string? Proforma, string? Timbrado);
