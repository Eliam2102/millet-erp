using MediatR;

namespace Millet.Compras.Application.Oc.CrearOrdenCompraDesdeRequisicion;

/// <summary>
/// Crea una OC desde una RQ Autorizada (flujo §4.1 del mapa funcional).
/// Pre-llena la cabecera con datos heredados de la RQ (sucursal,
/// almacén destino, departamento del solicitante para la línea, etc.)
/// y agrega 1 línea por cada línea de RQ con FK a
/// (RequisicionId, LineaRequisicionId).
///
/// El handler marca la RQ como <c>ComprometidaEnOcId</c> en la misma TX
/// y publica un <c>RqComprometidaEnOcEvent</c> tras SaveChanges.
///
/// Con <c>CotizacionId</c> (F1-COM-02, decisión D1) la OC toma precio y moneda de la
/// cotización elegida, esta queda marcada con <c>MotivoEleccion</c> y su PDF se copia
/// a la OC como adjunto <c>cotizacion</c>; las demás cotizaciones se conservan.
/// </summary>
public sealed record CrearOrdenCompraDesdeRequisicionCommand(
    Guid RequisicionId,
    string SucursalCodigo,
    short FolioAnio,
    Guid ProveedorId,
    Guid CondicionesPagoId,
    Guid UsoPrincipalId,
    DateOnly FechaDocumento,
    string Moneda = "MXN",
    decimal? TipoCambio = null,
    bool EsImportacion = false,
    DateOnly? FechaEntregaEsperada = null,
    string? Observaciones = null,
    Guid? CotizacionId = null,
    string? MotivoEleccion = null,
    string? UsoCfdiClave = null,
    string? MetodoPago = null,
    string? FormaPagoClave = null) : IRequest<CrearOrdenCompraDesdeRequisicionResponse>;

public sealed record CrearOrdenCompraDesdeRequisicionResponse(
    Guid OrdenCompraId,
    string Folio,
    short FolioAnio,
    int LineasHeredadas);
