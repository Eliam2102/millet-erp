using MediatR;

namespace Millet.Compras.Application.Oc.CrearOrdenCompraVacia;

/// <summary>
/// Comando para crear una orden de compra "vacía" (sin líneas, sin
/// adjuntos) en estado <c>Borrador</c>. Las líneas se agregan en F2-PR1
/// vía <c>AgregarLineaManualCommand</c> / <c>AgregarLineaDesdeRequisicionCommand</c>.
///
/// El cliente NO envía <c>EmpresaId</c> ni <c>CompradorTitularId</c>: ambos
/// los resuelve el handler desde el JWT (<c>ICurrentEmpresaContext.Current</c>
/// y <c>ICurrentUserContext.UserId</c>) para evitar cross-tenant injection
/// y suplantación del comprador.
///
/// <see cref="EncargadoComprasId"/> es opcional. Si <c>null</c>, el handler
/// usa el current user como encargado (= comprador titular). La reasignación
/// post-creación entra en F2-PR2 (<c>ActualizarCabeceraCommand</c>).
///
/// El folio se genera atómicamente en el handler combinando el prefijo
/// fijo <c>OC-</c> + <see cref="SucursalCodigo"/> + <see cref="FolioAnio"/>
/// + secuencia de <c>compras.folio_secuencias_oc</c> (formato §4.3 del
/// diseño).
///
/// Crear una OC con <see cref="SinRequisicionPrevia"/> = true requiere
/// el permiso <c>compras.ordenes.crear-sin-rq</c> además de
/// <c>compras.ordenes.crear</c>; la validación del permiso adicional vive
/// en el endpoint Api (mismo patrón que RQ con seleccionar-requisitante)
/// para no acoplar Compras a Identidad.
/// </summary>
public sealed record CrearOrdenCompraVaciaCommand(
    Guid SucursalDestinoId,
    string SucursalCodigo,
    short FolioAnio,
    Guid ProveedorId,
    Guid CondicionesPagoId,
    Guid UsoPrincipalId,
    DateOnly FechaDocumento,
    string Moneda = "MXN",
    decimal? TipoCambio = null,
    bool SinRequisicionPrevia = false,
    bool EsImportacion = false,
    bool CotizacionExcepcionada = false,
    string? Observaciones = null,
    string? MotivoSinRequisicion = null,
    DateOnly? FechaEntregaEsperada = null,
    Guid? EncargadoComprasId = null,
    Guid? OcOrigenId = null) : IRequest<CrearOrdenCompraVaciaResponse>;
