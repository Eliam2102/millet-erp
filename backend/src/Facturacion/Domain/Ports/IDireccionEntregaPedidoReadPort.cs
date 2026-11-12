namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Dirección (de A+W, <c>BW_AUFTR_KOPF</c>) de uno de los dos domicilios del pedido: la principal (<c>AH_</c>) o la de
/// entrega (<c>AL_</c>). Los campos vacíos vienen como <c>null</c>.
/// </summary>
public sealed record DireccionPedidoAw(string? Nombre, string? Calle, string? CodigoPostal, string? Ciudad, string? Provincia, string? Pais);

/// <summary>Las dos direcciones del pedido tal como las expone la vista de integración.</summary>
public sealed record DireccionesPedidoAw(DireccionPedidoAw Principal, DireccionPedidoAw Entrega);

/// <summary>
/// M-354: lee las direcciones del pedido A+W para sugerir el destino del tramo. La implementación real lee la vista del ERP
/// que ajusta Integraciones (campos <c>AH_*</c> y <c>AL_*</c> de <c>BW_AUFTR_KOPF</c>, llave ID); mientras no exista, el
/// destino se captura a mano.
/// </summary>
public interface IDireccionEntregaPedidoReadPort
{
    Task<DireccionesPedidoAw?> ObtenerAsync(Guid pedidoFacturableId, CancellationToken cancellationToken);
}
