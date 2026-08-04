namespace Millet.Integraciones.Fiscal.Domain;

/// <summary>
/// Tipo de consulta SAT (catálogo <c>SatQueryTypes</c> de FiscalAPI,
/// validado contra <c>test.fiscalapi.com/api/v4/download-catalogs</c>).
/// </summary>
public enum SatQueryType
{
    /// <summary>Metadata: resumen del CFDI (UUID, RFCs, total, tipo, estatus). Barato.</summary>
    Metadata = 1,
    /// <summary>CFDI: XML completo parseado. Más caro en cuota PAC.</summary>
    Cfdi = 2,
    /// <summary>Retenciones: comprobantes de retenciones e información de pagos.</summary>
    Retenciones = 3,
}

/// <summary>Sentido de la descarga (catálogo <c>DownloadTypes</c>).</summary>
public enum DownloadType
{
    Emitidos = 1,
    Recibidos = 2,
    /// <summary>
    /// Consulta por UUID específico — reemplaza al <c>EstadoSatRefreshWorker</c>
    /// del diseño viejo (ver doc 02 §13.3 implicación 1).
    /// </summary>
    Uuid = 3,
}

/// <summary>
/// Estatus del CFDI en SAT al filtrar (catálogo <c>SatInvoiceStatuses</c>).
/// <c>Todos</c> (string vacío en FiscalAPI) trae Vigentes + Cancelados con
/// una sola rule — ver doc 02 §13.3 implicación 2.
/// </summary>
public enum SatInvoiceStatusFilter
{
    Todos = 0,
    Vigente = 1,
    Cancelado = 2,
}
