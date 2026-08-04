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
    Cliente = 2,
    Articulo = 3,
    Inventario = 4,
}
