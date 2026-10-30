using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain;

namespace Millet.Compras.Application.Oc.ObtenerOrdenCompraPorId;

/// <summary>
/// DTO con la cabecera de <see cref="OrdenCompra"/>. El campo
/// <see cref="Version"/> se expone también vía header <c>ETag</c>
/// (cuidado §2.4 [P1]) para que el cliente envíe <c>If-Match</c> en
/// mutaciones futuras.
///
/// <para>
/// UF3-PR1 extiende el response con las columnas shadow de F2-PR3
/// (referenciaProveedor, contactoProveedor*, infoLogistica*,
/// infoImport*, descuentoGlobal*, gastosAdicionales, redondeo). El
/// frontend las consume en los sub-tabs Logística/Importación/Financiera
/// del Tab "Información" del detalle.
/// </para>
///
/// <para>
/// Incluye líneas, adjuntos, firmas por ciclo y solicitudes de cancelación
/// con su historial; la consulta carga las entidades hijas explícitamente.
/// </para>
/// </summary>
public sealed record OrdenCompraResponse(
    Guid Id,
    Guid EmpresaId,
    string Folio,
    short FolioAnio,
    Guid ProveedorId,
    // Etiqueta del proveedor resuelta en backend (ADR-0042 addendum). Nullable →
    // el FE cae al id. Mapster las deja en null; el handler las puebla tras el
    // map (batch IProveedorReadPort).
    string? ProveedorRazonSocial,
    string? ProveedorClave,
    Guid SucursalDestinoId,
    Guid CondicionesPagoId,
    Guid UsoPrincipalId,
    string Moneda,
    decimal? TipoCambio,
    Guid CompradorTitularId,
    Guid EncargadoComprasId,
    string? Observaciones,
    bool SinRequisicionPrevia,
    bool EsImportacion,
    bool CotizacionExcepcionada,
    DateOnly FechaDocumento,
    DateTimeOffset? FechaContabilizacion,
    DateOnly? FechaEntregaEsperada,
    EstadoOrdenCompra Estado,
    SubEstadoRecepcion SubEstadoRecepcion,
    SubEstadoFacturacion SubEstadoFacturacion,
    SubEstadoPago SubEstadoPago,
    string? MotivoSinRequisicion,
    string? MotivoCancelacion,
    Guid? MotivoRechazoId,
    string? MotivoRechazoTexto,
    Guid? OcOrigenId,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // UF2-PR3-a: líneas del agregado expuestas en el detalle. Antes
    // este campo no existía y el editor de líneas (UF2-PR3-b) no
    // podía listar lo que el comprador agregaba. Mapster proyecta la
    // collection completa; el handler hace .Include(o => o.Lineas)
    // para cargarlas en la misma query.
    IReadOnlyList<LineaOrdenCompraResponse> Lineas,
    // UF3-PR2: adjuntos del agregado expuestos en el detalle (Tab
    // "Adjuntos"). Mapster proyecta la collection completa desde
    // OrdenCompra.Adjuntos; el handler hace .Include(o => o.Adjuntos)
    // para cargarlos en la misma query.
    IReadOnlyList<AdjuntoOcResponse> Adjuntos,
    // UF3-PR1: campos shadow F2-PR3 expuestos en el detalle (sub-tabs
    // Información/Logística/Importación/Financiera). Mapster los proyecta
    // por convención de nombres desde el agregado (mismas propiedades).
    string? ReferenciaProveedor,
    string? ContactoProveedorNombre,
    string? ContactoProveedorEmail,
    string? ContactoProveedorTelefono,
    string? InfoLogisticaDireccion,
    Guid? InfoLogisticaTransportistaId,
    string? InfoLogisticaTransportistaTexto,
    string? InfoLogisticaNumeroGuia,
    string? InfoLogisticaInstrucciones,
    Guid? InfoImportIncotermId,
    string? InfoImportPaisOrigen,
    string? InfoImportNumeroContenedor,
    string? InfoImportCodigoRuta,
    string? InfoImportSemanaEmbarque,
    string? InfoImportNumeroPedimento,
    // PLATFORM-TODO(F1-COM-06): supuesto Vilo, confirmar con lista de Millet.
    TipoOperacionComercioExterior? InfoImportTipoOperacion,
    string? InfoImportNumeroBl,
    TipoTransporte? InfoImportTipoTransporte,
    string? InfoImportAgenteAduanalNaviera,
    string? InfoImportPuertoAduanaEntrada,
    DateOnly? InfoImportFechaEstimadaArribo,
    bool InfoImportCertificadoOrigen,
    DescuentoTipo? DescuentoGlobalTipo,
    decimal? DescuentoGlobalValor,
    decimal GastosAdicionales,
    decimal Redondeo)
{
    public string? UsoCfdiClave { get; init; }
    public string? UsoCfdiDescripcion { get; init; }
    public string? MetodoPago { get; init; }
    public string? FormaPagoClave { get; init; }
    public string? FormaPagoDescripcion { get; init; }
    public int Impresiones { get; init; }
    public DateTimeOffset? UltimaImpresionEn { get; init; }
    public Guid? UltimaImpresionPor { get; init; }
    public string? UltimaImpresionPorNombre { get; init; }
    public IReadOnlyList<Millet.Almacen.Application.PublicPorts.RecepcionImportacionLectura> RecepcionesImportacion { get; init; } = [];
    public string? Obra { get; init; }
    public int CicloAutorizacion { get; init; }
    public IReadOnlyList<AutorizacionOcResponse> Autorizaciones { get; init; } = [];
    public IReadOnlyList<SolicitudCancelacionOcResponse> SolicitudesCancelacion { get; init; } = [];

    // F1-COM-11: estado comercial, compromiso vigente e historial de respuestas del proveedor (más reciente primero).
    public EstadoComercialProveedorOc EstadoComercialProveedor { get; init; }
    public DateOnly? FechaCompromisoProveedor { get; init; }

    // 86e3tacbn: conformidad del servicio (una por OC); «no conforme» y última reversa visibles para Compras.
    public decimal MontoPagado { get; init; }
    public DateTimeOffset? ServicioConfirmadoEn { get; init; }
    public Guid? ServicioConfirmadoPorId { get; init; }
    public string? ServicioConfirmadoPorNombre { get; init; }
    public string? ServicioConfirmadoComentario { get; init; }
    public DateTimeOffset? ServicioNoConformeEn { get; init; }
    public string? ServicioNoConformePorNombre { get; init; }
    public string? ServicioNoConformeMotivo { get; init; }
    public DateTimeOffset? ServicioConfirmacionRevocadaEn { get; init; }
    public string? ServicioConfirmacionRevocadaMotivo { get; init; }
    public IReadOnlyList<RespuestaProveedorOcResponse> RespuestasProveedor { get; init; } = [];

    /// <summary>Incidencias del proveedor por línea (F1-COM-07), la más reciente primero.</summary>
    public IReadOnlyList<Incidencias.IncidenciaProveedorOcResponse> Incidencias { get; init; } = [];

    /// <summary>
    /// Campos de cabecera que siguen en TBD; la OC no se puede transmitir mientras la lista no esté vacía.
    /// Valores posibles: <c>proveedor</c>, <c>condicionesPago</c>, <c>usoPrincipal</c>,
    /// <c>usoCfdiClave</c>, <c>metodoPago</c>, <c>formaPagoClave</c>.
    /// </summary>
    public IReadOnlyList<string>? CamposPorCapturar { get; init; }

    /// <summary>F1-COM-02: cotización elegida que originó la OC (null si se creó sin comparativo).</summary>
    public CotizacionElegidaOcResponse? CotizacionElegida { get; init; }

    /// <summary>
    /// Indicador de recurrencia: el proveedor y el comprador titular de esta OC suman al menos el
    /// mínimo de OC no canceladas en la ventana de recurrencia (60 días); la propia OC cuenta.
    /// </summary>
    public bool EsRecurrente { get; init; }
}

public sealed record AutorizacionOcResponse(Guid Id, int Ciclo, NivelAutorizacion Nivel,
    ResultadoAutorizacionOc Resultado, Guid UsuarioId, DateTimeOffset FechaHora,
    Guid? MotivoRechazoId, string? MotivoRechazoTexto, string? Notas)
{
    public string? MotivoRechazoNombre { get; init; }
}

public sealed record SolicitudCancelacionOcResponse(Guid Id, Guid SolicitanteId,
    DateTimeOffset FechaSolicitud, Guid MotivoCancelacionId, string MotivoSolicitud,
    Guid? ResolutorId, DateTimeOffset? FechaResolucion, bool? Confirmada, string? MotivoResolucion);

/// <summary>Precio cotizado por unidad de la requisición de una partida (para avisar si la línea de la OC se aparta de él).</summary>
public sealed record PrecioCotizadoLineaResponse(Guid LineaRequisicionId, decimal PrecioPorUnidadRequisicion);

public sealed record CotizacionElegidaOcResponse(
    Guid CotizacionId, Guid RequisicionId, Guid ProveedorId, string? FolioProveedor, string MotivoEleccion, DateTimeOffset ElegidaEn,
    IReadOnlyList<PrecioCotizadoLineaResponse> Precios);

public sealed record CompromisoEntregaOcResponse(Guid Id, Guid LineaOrdenCompraId, decimal Cantidad, DateOnly FechaPrometida);

public sealed record RespuestaProveedorOcResponse(Guid Id, int Numero, TipoRespuestaProveedorOc? Tipo, DateOnly? FechaRespuesta, string? Evidencia, string? ContactoProveedor, string? Motivo, bool TieneDiferencias, Guid RegistradaPorId, DateTimeOffset RegistradaEn, bool Vigente, IReadOnlyList<CompromisoEntregaOcResponse> Compromisos);
