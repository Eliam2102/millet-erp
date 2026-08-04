namespace Millet.Integraciones.Fiscal.Domain.Ports;

/// <summary>
/// Solicitud estructurada de emisión de un CFDI 4.0 (contrato PAC-neutral del
/// puerto <see cref="ICfdiTimbradoPort"/>). Facturación la construye desde sus
/// agregados con <c>CfdiEmisionBuilder</c>; el adapter del PAC la mapea a su
/// modelo (FiscalAPI: <c>Invoice</c>) sin conocer el dominio de Facturación.
///
/// <para>
/// La <see cref="FechaLocal"/> viaja YA convertida a la zona horaria fiscal
/// (<c>America/Mexico_City</c>) — el SDK del PAC no convierte husos y el SAT
/// rechaza fechas fuera de rango (error 401 del probe sandbox, doc 02 §13.4).
/// Las tasas de impuesto van como <c>decimal</c> con 6 decimales (CFDI40179).
/// </para>
/// <para>
/// <see cref="EmpresaId"/> resuelve las credenciales PAC por empresa
/// (<c>ConfiguracionPac</c>) en el adapter real.
/// </para>
/// </summary>
public sealed record CfdiEmision(
    Guid EmpresaId,
    TipoCfdi Tipo,
    EmisorCfdi Emisor,
    ReceptorCfdi Receptor,
    string Serie,
    string Folio,
    DateTimeOffset FechaLocal,
    string Moneda,
    decimal? TipoCambio,
    string FormaPago,
    string MetodoPago,
    string Exportacion,
    IReadOnlyList<ConceptoCfdi> Conceptos,
    IReadOnlyList<RelacionCfdiEmision> Relaciones,
    ComplementoPagoCfdi? ComplementoPago = null,
    ComplementoCceCfdi? ComplementoCce = null,
    ComplementoCartaPorteCfdi? ComplementoCartaPorte = null,
    Guid? ReferenciaInterna = null);

/// <summary>Tipo de comprobante CFDI (c_TipoDeComprobante: I, E, P, T).</summary>
public enum TipoCfdi
{
    Ingreso,
    Egreso,
    Pago,
    Traslado,
}

/// <summary>
/// Emisor del CFDI. <paramref name="Nombre"/> debe ir SIN régimen societario
/// ("SA DE CV") — CFDI40139; <paramref name="LugarExpedicion"/> es el CP fiscal
/// de la empresa (obligatorio CFDI 4.0).
/// </summary>
public sealed record EmisorCfdi(
    string Rfc,
    string Nombre,
    string RegimenFiscal,
    string LugarExpedicion);

/// <summary>Receptor del CFDI (snapshot al emitir). <paramref name="NumRegIdTrib"/> solo para extranjeros (CCE).</summary>
public sealed record ReceptorCfdi(
    string Rfc,
    string Nombre,
    string RegimenFiscal,
    string CodigoPostal,
    string UsoCfdi,
    string Pais,
    bool EsGenerico,
    string? NumRegIdTrib = null);

/// <summary>
/// Concepto (línea) del CFDI con su desglose de impuestos y pedimento
/// opcional (art. 29-A). <paramref name="NoIdentificacion"/> es el SKU /
/// código interno del producto (atributo <c>NoIdentificacion</c> del CFDI;
/// <c>itemSku</c> en FiscalAPI, obligatorio en su validación) — cuando el
/// agregado origen no persiste código propio, el adapter usa
/// <c>ClaveProdServ</c> como fallback.
/// </summary>
public sealed record ConceptoCfdi(
    string ClaveProdServ,
    string ClaveUnidad,
    decimal Cantidad,
    string Descripcion,
    decimal ValorUnitario,
    decimal Importe,
    decimal Descuento,
    string ObjetoImp,
    IReadOnlyList<ImpuestoConceptoCfdi> Impuestos,
    PedimentoCfdi? Pedimento = null,
    string? NoIdentificacion = null);

/// <summary>
/// Impuesto de un concepto. <paramref name="Impuesto"/> = c_Impuesto (001 ISR,
/// 002 IVA); <paramref name="TasaOCuota"/> con 6 decimales (CFDI40179);
/// <paramref name="EsRetencion"/> distingue traslado vs retención.
/// </summary>
public sealed record ImpuestoConceptoCfdi(
    decimal Base,
    string Impuesto,
    string TipoFactor,
    decimal TasaOCuota,
    decimal Importe,
    bool EsRetencion);

/// <summary>Información aduanera de un concepto (número de pedimento, §3.bis.5).</summary>
public sealed record PedimentoCfdi(
    string Numero,
    DateOnly? FechaDocAduanero = null,
    string? IdentificacionMercancia = null);

/// <summary>Relación a otro CFDI por UUID (nodo CfdiRelacionados; tipos 01/03/04/07).</summary>
public sealed record RelacionCfdiEmision(
    string TipoRelacion,
    string Uuid);

// ---- Complemento de Recepción de Pagos 2.0 (CFDI tipo P) ----

/// <summary>Complemento Pago 2.0: un pago que cubre una o varias facturas PPD.</summary>
public sealed record ComplementoPagoCfdi(
    DateTimeOffset FechaPago,
    string FormaPago,
    string Moneda,
    decimal? TipoCambio,
    decimal Monto,
    string? CuentaOrdenante,
    string? CuentaBeneficiaria,
    string? ReferenciaPago,
    IReadOnlyList<DocumentoPagoCfdi> Documentos);

/// <summary>
/// Nodo DoctoRelacionado del complemento Pago 2.0. Serie/Folio son opcionales
/// en el SAT. <c>ObjetoImpDR</c>=="02" obliga a poblar <c>Impuestos</c> con el
/// desglose (ImpuestosDR): base/tasa por impuesto, prorrateado al importe
/// pagado y expresado en la moneda del documento relacionado (la factura).
/// </summary>
public sealed record DocumentoPagoCfdi(
    string FacturaUuid,
    string? Serie,
    string? Folio,
    string Moneda,
    int NumParcialidad,
    decimal SaldoAnterior,
    decimal ImportePagado,
    decimal SaldoInsoluto,
    // c_ObjetoImp del DoctoRelacionado: 01 No objeto, 02 Sí objeto (exige ImpuestosDR), 03 Sí objeto no obligado.
    string ObjetoImpDR = "01",
    // EquivalenciaDR: unidades de la moneda del DR por 1 unidad de la moneda del pago (1 si es la misma).
    decimal Equivalencia = 1m,
    // Suma de las bases gravadas (traslados) del pago — <c>Subtotal</c> del DoctoRelacionado.
    decimal Subtotal = 0m,
    IReadOnlyList<ImpuestoDocumentoPagoCfdi>? Impuestos = null);

/// <summary>
/// Impuesto del DoctoRelacionado (nodo TrasladoDR/RetencionDR de ImpuestosDR).
/// La base es la porción del pago gravada por este impuesto, en la moneda de la
/// factura; FiscalAPI calcula el ImporteDR = Base × TasaOCuota.
/// </summary>
public sealed record ImpuestoDocumentoPagoCfdi(
    // c_Impuesto SAT: 001 ISR, 002 IVA, 003 IEPS.
    string Impuesto,
    // c_TipoFactor: Tasa | Cuota | Exento.
    string TipoFactor,
    decimal TasaOCuota,
    bool EsRetencion,
    decimal BaseDR);

// ---- Complemento de Comercio Exterior 2.0 (exportación definitiva) ----

/// <summary>
/// Complemento CCE 2.0 (§4.5): INCOTERM, TC DOF, clave de pedimento,
/// certificado de origen, domicilio del receptor extranjero y mercancías con
/// fracción arancelaria (F12-PR3 lo completa para el timbrado real).
/// </summary>
public sealed record ComplementoCceCfdi(
    string TipoOperacion,
    string Incoterm,
    decimal TipoCambioUsd,
    string ReceptorNumRegIdTrib,
    string ReceptorPaisResidencia,
    string? ClaveDePedimento,
    bool CertificadoOrigen,
    DomicilioCceCfdi? Domicilio,
    IReadOnlyList<MercanciaCceCfdi> Mercancias);

/// <summary>Domicilio del receptor extranjero (nodo Domicilio del CCE 2.0).</summary>
public sealed record DomicilioCceCfdi(
    string? Calle,
    string Estado,
    string Pais,
    string CodigoPostal);

/// <summary>Mercancía del CCE (fracción arancelaria + valores aduaneros en USD).
/// <paramref name="NoIdentificacion"/> correlaciona con el concepto de la factura.</summary>
public sealed record MercanciaCceCfdi(
    string NoIdentificacion,
    string FraccionArancelaria,
    string UnidadAduana,
    decimal CantidadAduana,
    decimal ValorUnitarioAduana,
    decimal ValorDolares);

// ---- Complemento Carta Porte 3.1 (autotransporte federal) ----

/// <summary>
/// Complemento Carta Porte 3.1 (§4.6): tramo origen→destino con autotransporte,
/// operador y mercancías. <paramref name="IdCcpRelacionado"/> referencia la
/// Carta Porte previa en multi-tramo (invariante 11). Remitente = emisor del
/// CFDI y destinatario = receptor (mercancía propia / servicio al cliente).
/// </summary>
public sealed record ComplementoCartaPorteCfdi(
    UbicacionCartaPorteCfdi Origen,
    UbicacionCartaPorteCfdi Destino,
    string RfcRemitente,
    string NombreRemitente,
    string RfcDestinatario,
    string NombreDestinatario,
    decimal DistanciaKm,
    DateTimeOffset FechaSalida,
    DateTimeOffset FechaLlegadaEstimada,
    VehiculoCartaPorteCfdi Vehiculo,
    OperadorCartaPorteCfdi Operador,
    string? IdCcpRelacionado,
    IReadOnlyList<MercanciaCartaPorteCfdi> Mercancias);

/// <summary>
/// Ubicación del tramo con su domicilio SAT (CP 3.1 exige Domicilio en Origen y
/// Destino). <paramref name="Estado"/> = <c>c_Estado</c> (3 chars, p.ej. QUE);
/// <paramref name="Pais"/> = MEX en el MVP.
/// </summary>
public sealed record UbicacionCartaPorteCfdi(
    string Descripcion,
    string CodigoPostal,
    string Estado,
    string Pais);

/// <summary>Autotransporte del tramo (placa, configuración vehicular, peso bruto, permiso SCT, seguro).</summary>
public sealed record VehiculoCartaPorteCfdi(
    string Placa,
    string ConfigVehicular,
    int AnioModelo,
    decimal? PesoBrutoVehicular,
    string? TipoPermisoSct,
    string? NumPermisoSct,
    string? Aseguradora,
    string? PolizaSeguro);

/// <summary>Operador (chofer) del tramo.</summary>
public sealed record OperadorCartaPorteCfdi(
    string Rfc,
    string Nombre,
    string NumLicencia);

/// <summary>Mercancía transportada del tramo.</summary>
public sealed record MercanciaCartaPorteCfdi(
    string BienesTransp,
    string Descripcion,
    string ClaveUnidad,
    decimal Cantidad,
    decimal PesoEnKg,
    bool MaterialPeligroso);
