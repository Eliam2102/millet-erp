using Millet.Facturacion.Domain.Facturas;

namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Puerto de lectura de órdenes facturables de Planta Pintura (§9, §12.1,
/// F10-PR2). El <c>PlantaPinturaImportWorker</c> las consume y las envía a
/// <c>ImportarPedidoPlantaPinturaCommand</c>. A diferencia de A+W, Planta Pintura
/// <b>exige master preexistente</b> (sin auto-provisión). Adaptador real:
/// <c>PinturasPedidosAdapter</c> (Integraciones.Aw, I13.9) sobre <c>dbo.pinturas_pedido</c>
/// de <c>MILLET_INTEGRACION</c>; sin conexión queda el stub vacío.
/// </summary>
public interface IPlantaPinturaPedidosReader
{
    Task<IReadOnlyList<PedidoPlantaPintura>> LeerPendientesAsync(int max, CancellationToken cancellationToken);

    /// <summary>
    /// Acuse en la misma fila de la orden (resultado, pedido ERP, error, fecha): la
    /// orden deja de leerse como pendiente. Mismo patrón que <c>OtrTablasAdapter.MarcarResultadoAsync</c>.
    /// </summary>
    Task MarcarResultadoAsync(
        string numeroPedido, ResultadoAcusePlantaPintura resultado, Guid? pedidoId, string? detalleError,
        DateTimeOffset fecha, CancellationToken cancellationToken);
}

/// <summary>Valor escrito en la columna de resultado de la fila de origen.</summary>
public enum ResultadoAcusePlantaPintura : short
{
    Creado = 1,
    Error = 2,
}

/// <summary>
/// Orden facturable de Planta Pintura leída del Sistema de Origenes.
/// <c>CanalVenta</c> es el id del catálogo <c>compartido.canales_venta</c>
/// (FAC-ING-PR2) — para este origen es fijo el canal 9 "Planta Pintura"
/// (<see cref="CanalesVentaConocidos.PlantaPintura"/>).
/// <c>Lineas</c> = una por línea de la orden (<c>pinturas_pedido_linea</c>, script 16 v3); <c>FechaPedido</c> = fecha
/// de la orden en el portal (<c>pinturas_pedido.fecha_pedido</c>), fecha del documento de venta.
/// <c>EmpleadoCapturaClave</c> (obligatoria) y <c>VendedorClave</c> (opcional) = claves de <c>Empleado.Clave</c> del
/// usuario del portal que capturó y del vendedor (script 16 v4, trazabilidad); la ingesta las valida contra el catálogo.
/// </summary>
public sealed record PedidoPlantaPintura(
    string NumeroPedido,
    Guid SucursalId,
    string ClienteRef,
    string ClienteNombre,
    short CanalVenta,
    ComportamientoFiscal ComportamientoFiscal,
    string Moneda,
    long? ObraId,
    string? ObraNombre,
    string? Comentarios,
    long VersionOrigen,
    string? EstadoOrigen,
    IReadOnlyList<LineaPedidoPlantaPintura> Lineas,
    string PayloadCrudo,
    DateOnly? FechaPedido = null,
    string? EmpleadoCapturaClave = null,
    string? VendedorClave = null);

public sealed record LineaPedidoPlantaPintura(
    string ProductoRef,
    string Descripcion,
    string? ClaveProdServSat,
    string? ClaveUnidadSat,
    decimal Cantidad,
    decimal Precio,
    decimal Descuento,
    bool RequierePedimento,
    /// <summary>Tasa de IVA con la que se cotizó (0.16 = 16 %); null = no informada, no se valida.</summary>
    decimal? TasaIva = null);
