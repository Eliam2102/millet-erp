using Millet.Facturacion.Domain.Facturas;

namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Puerto de lectura de órdenes facturables de Planta Pintura (§9, §12.1,
/// F10-PR2). El <c>PlantaPinturaImportWorker</c> las consume y las envía a
/// <c>ImportarPedidoPlantaPinturaCommand</c>. A diferencia de A+W, Planta Pintura
/// <b>exige master preexistente</b> (sin auto-provisión). Dueño real:
/// <c>Integraciones.Origenes</c> (vía Hybrid Connection); en dev es stub vacío
/// (<c>PLATFORM-TODO(&lt;PlantaPinturaOrigenes&gt;)</c>).
/// </summary>
public interface IPlantaPinturaPedidosReader
{
    Task<IReadOnlyList<PedidoPlantaPintura>> LeerPendientesAsync(int max, CancellationToken cancellationToken);
}

/// <summary>
/// Orden facturable de Planta Pintura leída del Sistema de Origenes.
/// <c>CanalVenta</c> es el id del catálogo <c>compartido.canales_venta</c>
/// (FAC-ING-PR2) — para este origen es fijo el canal 9 "Planta Pintura"
/// (<see cref="CanalesVentaConocidos.PlantaPintura"/>).
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
    string PayloadCrudo);

public sealed record LineaPedidoPlantaPintura(
    string ProductoRef,
    string Descripcion,
    string? ClaveProdServSat,
    string? ClaveUnidadSat,
    decimal Cantidad,
    decimal Precio,
    decimal Descuento,
    bool RequierePedimento);
