namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Modelo de impresión del CFDI con el formato de Millet (FMT-4, 86e3ukucq):
/// factura de venta, factura de anticipo y nota de crédito. Lo arma
/// <c>CfdiPdfModelBuilder</c> desde la entidad y los <see cref="DatosImpresionCfdi"/>;
/// la plantilla QuestPDF solo lo presenta y nunca toca entidades ni la base.
/// </summary>
public sealed record CfdiPdfModel(
    TipoDocumentoPdf Tipo,
    string TituloEs,
    string TituloEn,
    string Serie,
    string Folio,
    DateTimeOffset FechaEmision,
    string TipoComprobante,
    EmisorPdf Emisor,
    ReceptorPdf Receptor,
    CfdiCabeceraPdf Cabecera,
    IReadOnlyList<ConceptoPdf> Conceptos,
    IReadOnlyList<ImpuestoResumenPdf> Impuestos,
    TotalesFisicosPdf TotalesFisicos,
    string? Comentarios,
    TotalesPdf Totales,
    MontoEnLetraPdf MontoEnLetra,
    PagoPdf Pago,
    IReadOnlyList<CfdiRelacionadoPdf> Relacionados,
    TimbrePdf? Timbre,
    string NombreArchivo,
    ComplementoPagoPdf? ComplementoPago = null,
    ComercioExteriorPdf? ComercioExterior = null);

public enum TipoDocumentoPdf
{
    Factura = 1,
    Anticipo = 2,
    NotaCredito = 3,
    ComplementoPago = 4,
}

/// <summary>Emisor con domicilio en líneas y logo (FMT-1a). Sin logo, la plantilla imprime la razón social.</summary>
public sealed record EmisorPdf(
    string RazonSocial,
    string? NombreComercial,
    string Rfc,
    string Regimen,
    IReadOnlyList<string> DomicilioLineas,
    byte[]? Logo);

public sealed record ReceptorPdf(
    string Nombre,
    string Rfc,
    string CodigoPostalFiscal,
    string Regimen,
    string Pais,
    string? CondicionesPago);

/// <summary>Recuadro «CFDI»: datos de certificación y referencias del cliente (FMT-3).</summary>
public sealed record CfdiCabeceraPdf(
    string LugarExpedicion,
    string Moneda,
    decimal? TipoCambio,
    string? OrdenCompraCliente,
    string? Referencia,
    string? NoCertificadoEmisor,
    string? NoCertificadoSat,
    DateTimeOffset? FechaTimbrado,
    string? Uuid);

/// <param name="CodigoArticulo">Código del artículo (A+W) si la línea lo tiene; se imprime «código / clave SAT».</param>
public sealed record ConceptoPdf(
    string? CodigoArticulo,
    string ClaveProdServ,
    string Descripcion,
    string? LineaReferencia,
    decimal Cantidad,
    string ClaveUnidad,
    decimal ValorUnitario,
    decimal Descuento,
    decimal Importe);

/// <param name="Nombre">Clave SAT y nombre del impuesto, p. ej. «002 IVA».</param>
public sealed record ImpuestoResumenPdf(string Nombre, string TipoFactor, decimal TasaOCuota, decimal Base, decimal Importe, bool EsRetencion);

/// <summary>Línea «Total Kg · Total Pie² · Total Qty» que el formato imprime en los tres documentos.</summary>
public sealed record TotalesFisicosPdf(decimal? Kg, decimal? Pie2, decimal Piezas);

public sealed record TotalesPdf(string Moneda, decimal Subtotal, decimal Descuento, decimal Impuestos, decimal Retenciones, decimal Total);

public sealed record MontoEnLetraPdf(string Es, string En);

/// <summary>Uso CFDI, método y forma de pago ya resueltos como «clave - descripción».</summary>
public sealed record PagoPdf(string UsoCfdi, string MetodoPago, string FormaPago);

public sealed record CfdiRelacionadoPdf(string Uuid, string TipoRelacion);

/// <summary>
/// Datos del pago del complemento de pago (CFDI tipo P, Pago 2.0; 86e3tfdx0). Forma
/// de pago ya como «clave - descripción»; cuentas y número de operación si los hay.
/// </summary>
public sealed record ComplementoPagoPdf(
    DateTimeOffset FechaPago,
    string FormaPago,
    string Moneda,
    decimal? TipoCambio,
    decimal Monto,
    string? NumeroOperacion,
    string? CuentaOrdenante,
    string? CuentaBeneficiaria,
    IReadOnlyList<DocumentoPagadoPdf> Documentos);

/// <summary>Factura cubierta por el pago (DoctoRelacionado): folio fiscal, parcialidad y saldos.</summary>
public sealed record DocumentoPagadoPdf(
    string Uuid,
    string? SerieFolio,
    string Moneda,
    int Parcialidad,
    decimal SaldoAnterior,
    decimal Pagado,
    decimal SaldoInsoluto);

/// <summary>
/// Complemento de Comercio Exterior de la factura de exportación (86e3tfdx0):
/// INCOTERM, TC en USD, total en dólares y registro tributario del receptor.
/// </summary>
public sealed record ComercioExteriorPdf(
    string TipoOperacion,
    string Incoterm,
    string? ClavePedimento,
    bool CertificadoOrigen,
    decimal TipoCambioUsd,
    decimal TotalUsd,
    string NumRegIdTrib,
    string ResidenciaFiscal);

/// <summary>Sellos y cadena del TFD. <c>UrlQr</c> es null si el sello no permite armar el QR (timbrado stub).</summary>
public sealed record TimbrePdf(string SelloCfdi, string SelloSat, string CadenaOriginal, string? UrlQr);

/// <summary>
/// Datos que la entidad del comprobante no trae y que el PDF necesita:
/// domicilio y logo del emisor (FMT-1a) y descripciones de catálogos SAT.
/// Los lee <see cref="IDatosImpresionCfdiReadPort"/>; si faltan, el builder
/// imprime solo las claves.
/// </summary>
public sealed record DatosImpresionCfdi(
    string? NombreComercial,
    IReadOnlyList<string> DomicilioLineas,
    byte[]? Logo,
    IReadOnlyDictionary<string, string> Regimenes,
    IReadOnlyDictionary<string, string> UsosCfdi,
    IReadOnlyDictionary<string, string> FormasPago)
{
    public static readonly DatosImpresionCfdi Vacio = new(
        null, [], null,
        new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>());
}

/// <summary>Lee los <see cref="DatosImpresionCfdi"/> del emisor y de los catálogos SAT (solo lectura).</summary>
public interface IDatosImpresionCfdiReadPort
{
    Task<DatosImpresionCfdi> ObtenerAsync(Guid empresaId, CancellationToken cancellationToken);

    /// <summary>Código del artículo (referencia A+W) por producto, para la columna «Código/ClavProdServ».</summary>
    Task<IReadOnlyDictionary<Guid, string>> CodigosArticuloAsync(IReadOnlyCollection<Guid> productoIds, CancellationToken cancellationToken);
}
