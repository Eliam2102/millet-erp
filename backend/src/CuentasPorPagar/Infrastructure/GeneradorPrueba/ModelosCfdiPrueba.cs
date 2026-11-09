namespace Millet.CuentasPorPagar.Infrastructure.GeneradorPrueba;

/// <summary>
/// Emisor para el CFDI sintético de prueba (proveedor de la OC).
/// </summary>
public sealed record EmisorCfdiPrueba(
    string Rfc,
    string RazonSocial,
    string RegimenFiscal = "601");

/// <summary>
/// Receptor para el CFDI sintético de prueba (empresa compradora Millet).
/// </summary>
public sealed record ReceptorCfdiPrueba(
    string Rfc,
    string RazonSocial,
    string? CodigoPostal = null,
    string RegimenFiscal = "601",
    string UsoCfdi = "G03");

/// <summary>
/// Partida de concepto para el CFDI sintético de prueba.
/// </summary>
public sealed record LineaCfdiPrueba(
    int Posicion,
    decimal Cantidad,
    decimal PrecioUnitario,
    string? ArticuloClave = null,
    string? ArticuloNombre = null,
    string? DescripcionExtendida = null,
    string ClaveProdServ = "01010101",
    string ClaveUnidad = "H87",
    string UnidadMedida = "PZA",
    decimal Descuento = 0m,
    decimal TasaIva = 0.16m,
    decimal RetencionIsr = 0m,
    decimal? TasaRetencionIsr = null);

/// <summary>
/// Parámetros de entrada para la generación de un CFDI 4.0 sintético de prueba.
/// </summary>
public sealed record DatosGeneracionCfdiPrueba(
    EmisorCfdiPrueba Emisor,
    ReceptorCfdiPrueba Receptor,
    IReadOnlyList<LineaCfdiPrueba> Lineas,
    string? Folio = null,
    string Serie = "PRUEBA",
    string Moneda = "MXN",
    decimal? TipoCambio = null,
    string MetodoPago = "PPD",
    string? FormaPago = null,
    string? UsoCfdi = null,
    DateTimeOffset? Fecha = null,
    Guid? Uuid = null,
    string TipoDeComprobante = "I",
    string Exportacion = "01",
    string? LugarExpedicion = null);
