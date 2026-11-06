namespace Millet.Almacen.Application.Taller;

/// <summary>
/// Una posición de un pedido interno Serie 9000 leída del origen de A+W (solo lectura). Medidas en mm de la hoja;
/// <c>Cantidad</c> = hojas. <c>ClaveSucursal</c> es la clave A+W (numero_sucursal) del pedido.
/// </summary>
public sealed record PedidoInternoLineaAw(string NumeroPedido, int Posicion, string ArticuloRef, decimal Cantidad,
    decimal? AnchoMm, decimal? AltoMm, string? ClaveSucursal, DateOnly Fecha);

/// <summary>Lectura de lo que ya sirvió el taller de una sucursal en un día (pedidos de taller, estatus 15, ya ingeridos en Facturación).</summary>
public interface IPedidosTallerDiaReadPort
{
    Task<int> PedidosServidosAsync(Guid sucursalId, DateOnly fecha, CancellationToken ct);
}
