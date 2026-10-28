namespace Millet.Compartido.Application.Ports;

/// <summary>
/// ¿Hay una línea de OC abierta con medida de hoja para el artículo? La implementa Compras. La usa
/// Datos Maestros para no quitar ni cambiar el producto A+W de un artículo con compras en curso.
/// Abierta = OC que no está Cancelada ni Cerrada y línea con cantidad pendiente de recibir.
/// </summary>
public interface IOcLineaAbiertaConMedidaReadPort
{
    Task<bool> ArticuloTieneLineaAbiertaConMedidaAsync(Guid articuloId, CancellationToken cancellationToken);
}
