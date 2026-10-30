namespace Millet.ProductoTerminado.Application.Ports;

/// <summary>
/// Puerto inverso: Facturación conserva la única fuente del pedido ingerido de A+W;
/// Producto Terminado lo lee solo por aquí, nunca por sus tablas.
/// </summary>
public interface IPedidosPtReadPort
{
    /// <summary>Devuelve el pedido A+W de la empresa o <c>null</c> si aún no ha sido ingerido.</summary>
    Task<PedidoPtOrigen?> ObtenerPorNumeroAsync(Guid empresaId, string numeroPedido, CancellationToken ct);
}

/// <summary>Pedido tal como lo ve PT. <c>Estado</c> es el nombre del estado en Facturación (<c>Cancelado</c> = cancelado).</summary>
public sealed record PedidoPtOrigen(
    Guid PedidoFacturableId,
    string NumeroPedido,
    Guid SucursalId,
    string Estado,
    string? CondicionPagoOrigen,
    long? VersionOrigen,
    IReadOnlyList<LineaPtOrigen> Lineas);

/// <summary>Línea (posición) del pedido con cantidad y unidad SAT para derivar las piezas esperadas.</summary>
public sealed record LineaPtOrigen(
    int Posicion,
    Guid? ProductoId,
    string Descripcion,
    decimal Cantidad,
    string? ClaveUnidadSat);
