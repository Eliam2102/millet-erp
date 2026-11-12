namespace Millet.Integraciones.Aw.Domain;

/// <summary>
/// Discriminador del tipo de entidad sincronizada con A+W. Permite que
/// una sola tabla <c>entidad_externa</c> contenga distintos dominios.
/// Persistido como string en columna <c>tipo_entidad</c> (snake_case).
/// </summary>
public enum TipoEntidad : short
{
    Cotizacion = 0,
    Pedido = 1,
    // 2 y 3 (Cliente, Articulo) retirados (M-444): A+W no recibe altas de clientes, obras ni artículos desde el ERP.
    Inventario = 4,
}
