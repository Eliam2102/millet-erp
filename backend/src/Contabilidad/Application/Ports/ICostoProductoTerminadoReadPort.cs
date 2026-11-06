namespace Millet.Contabilidad.Application.Ports;

/// <summary>
/// Puerto de CONSUMIDOR (PCV · 86e3ke10b): costo de lo entregado de un pedido de producto terminado, para la póliza de costo de ventas.
/// Lo implementará el dueño de la valuación (Producto Terminado, PT5.12: costo de MP + desperdicio absorbido al arranque, V33; costo
/// completo con minutos de máquina después). Devuelve <c>null</c> si el pedido aún no tiene valuación: el motor manda el evento a la
/// bandeja de rechazos como dato faltante y se reprocesa cuando exista.
/// </summary>
public interface ICostoProductoTerminadoReadPort
{
    /// <summary>Costo total (MXN) de las piezas entregadas de cada posición, la sucursal del pedido y su tipo de producto.</summary>
    Task<CostoEntregaPt?> CostoEntregaAsync(Guid empresaId, Guid pedidoPtId, IReadOnlyList<PiezasPosicionPt> entregadas, CancellationToken ct);
}

/// <summary>Piezas entregadas de una posición del pedido.</summary>
public sealed record PiezasPosicionPt(int Posicion, int Piezas);

/// <summary>Valuación de una entrega: sucursal del pedido y costo por posición.</summary>
public sealed record CostoEntregaPt(Guid SucursalId, IReadOnlyList<CostoPosicionPt> Posiciones);

/// <summary>
/// Costo total (MXN) de las piezas entregadas de la posición. <see cref="TipoProducto"/> es opcional: con él la plantilla usa el rol
/// <c>COSTO_DE_VENTAS_{tipo}</c> si Contabilidad lo mapea (cuenta de costo de ventas por tipo de producto); si no, el rol base.
/// </summary>
public sealed record CostoPosicionPt(int Posicion, decimal Costo, string? TipoProducto = null);
