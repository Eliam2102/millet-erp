using MediatR;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Application.Oc.ListarOrdenesCompra;

/// <summary>
/// Bandeja principal de OCs (F6-PR3). Filtros opcionales sobre estado,
/// sub-estados, proveedor, comprador, fechas, importe y referencia
/// proveedor. Paginación offset-based; el orden default es por
/// <c>FechaDocumento DESC</c> + <c>Folio DESC</c> para que las OCs
/// recientes salgan primero.
/// </summary>
public sealed record ListarOrdenesCompraQuery(
    EstadoOrdenCompra? Estado = null,
    SubEstadoRecepcion? SubEstadoRecepcion = null,
    // Conveniencia: solo OCs con recepción pendiente (SubEstadoRecepcion !=
    // Completa, i.e. SinRecepcion o Parcial). El filtro SubEstadoRecepcion
    // de arriba es por valor exacto; este expresa "≠ Completa" sin pedir 2
    // queries. Lo usa el selector de Nueva recepción para no listar OCs ya
    // recibidas al 100%.
    bool? SoloConPendienteRecepcion = null,
    SubEstadoFacturacion? SubEstadoFacturacion = null,
    SubEstadoPago? SubEstadoPago = null,
    Guid? ProveedorId = null,
    Guid? CompradorTitularId = null,
    DateOnly? FechaDocumentoDesde = null,
    DateOnly? FechaDocumentoHasta = null,
    string? ReferenciaProveedor = null,
    int Page = 1,
    int PageSize = 50) : IRequest<ListarOrdenesCompraResponse>;

public sealed record ListarOrdenesCompraResponse(
    IReadOnlyList<OrdenCompraResumen> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record OrdenCompraResumen(
    Guid Id,
    string Folio,
    short FolioAnio,
    EstadoOrdenCompra Estado,
    SubEstadoRecepcion SubEstadoRecepcion,
    SubEstadoFacturacion SubEstadoFacturacion,
    SubEstadoPago SubEstadoPago,
    Guid ProveedorId,
    // Razón social del proveedor, resuelta server-side vía IProveedorReadPort
    // (aplicación de ADR-0042: ids→etiqueta por read-port batch, como el
    // detalle de OC). Evita que el cliente resuelva el nombre contra un
    // catálogo capado. Aditivo; null si no resuelve. Solo lo puebla
    // ListarOrdenesCompra; las demás proyecciones lo dejan en null.
    string? ProveedorNombre,
    // Sucursal destino de la OC. Aditivo (F-CxP): permite derivar la
    // sucursal al elegir la OC en la captura de factura (CapturarFacturaSheet)
    // sin pedirla a mano. Proyectado de OrdenCompra.SucursalDestinoId.
    Guid SucursalId,
    Guid CompradorTitularId,
    string Moneda,
    DateOnly FechaDocumento,
    string? ReferenciaProveedor);
