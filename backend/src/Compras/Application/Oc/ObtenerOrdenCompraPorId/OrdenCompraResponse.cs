using Millet.Compras.Domain.Oc;

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
/// Líneas, autorizaciones y adjuntos NO aparecen aquí porque F1-PR2 es
/// walking skeleton: el agregado solo tiene cabecera. F2-PR1 trae líneas,
/// F3-PR1 autorizaciones, F2-PR4 adjuntos — cada uno extiende este
/// response al promoverse.
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
    DescuentoTipo? DescuentoGlobalTipo,
    decimal? DescuentoGlobalValor,
    decimal GastosAdicionales,
    decimal Redondeo);
