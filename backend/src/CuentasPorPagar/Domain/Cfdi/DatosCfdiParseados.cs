namespace Millet.CuentasPorPagar.Domain.Cfdi;

/// <summary>
/// Resultado de parsear un XML CFDI 4.0. Estructura plana que el
/// handler de ingestión usa para construir un <see cref="CfdiRecibido"/>
/// (F1-PR1).
///
/// <para>
/// El parser **no extrae** addendas ni complementos en F1; los agrega
/// en PRs posteriores (CartaPorte, Pagos REPP, etc.). En F1 cubre la
/// cabecera (Comprobante + Emisor + Receptor + TimbreFiscalDigital),
/// los impuestos totales y, opcionalmente, las líneas de
/// <c>cfdi:Concepto</c>.
/// </para>
/// </summary>
public sealed record DatosCfdiParseados(
    string UuidCfdi,
    string RfcEmisor,
    string RazonSocialEmisor,
    string RfcReceptor,
    string RazonSocialReceptor,
    TipoCfdi Tipo,
    string? Folio,
    string? Serie,
    DateTimeOffset FechaCfdi,
    decimal Total,
    decimal Subtotal,
    decimal ImpuestosTrasladados,
    decimal Retenciones,
    string Moneda,
    decimal? TipoCambio,
    IReadOnlyList<LineaCfdiParseada> Lineas,
    // TES-PR8 [T-G11]: MetodoPago (PUE/PPD) del Comprobante — la fuente del
    // dato que viaja hasta Tesorería vía pasivo.autorizado-para-pago.v1
    // para detectar "pago PPD sin REPP recibido". Nullable: CFDIs 3.3 u
    // XML sin el atributo parsean sin romper.
    string? MetodoPago = null,
    // Nodos <cfdi:CfdiRelacionados> del Comprobante — prellenan la relación
    // (TipoRelacion 01/03/07 + UUID origen) al capturar NC/anticipo desde
    // el CFDI. Nullable: XML sin relaciones parsea sin romper.
    IReadOnlyList<CfdiRelacionadosParseados>? CfdiRelacionados = null);

/// <summary>
/// Un nodo <c>cfdi:CfdiRelacionados</c>: el tipo de relación SAT
/// (catálogo c_TipoRelacion — 01 NC, 03 devolución, 07 aplicación de
/// anticipo, etc.) y los UUID de los CFDI relacionados bajo él.
/// </summary>
public sealed record CfdiRelacionadosParseados(
    string TipoRelacion,
    IReadOnlyList<string> Uuids);

/// <summary>Línea de <c>cfdi:Concepto</c> mapeada para precarga al capturar (F1-PR1).</summary>
public sealed record LineaCfdiParseada(
    int Posicion,
    string ClaveProdServ,
    string? NoIdentificacion,
    decimal Cantidad,
    string ClaveUnidad,
    string? Unidad,
    string Descripcion,
    decimal ValorUnitario,
    decimal Importe,
    decimal? Descuento);
