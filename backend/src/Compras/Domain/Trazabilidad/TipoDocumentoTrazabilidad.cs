namespace Millet.Compras.Domain.Trazabilidad;

/// <summary>
/// Tipos de documento soportados por el árbol de trazabilidad cross-módulo
/// (F7-PR2). En v1 solo se conocen <see cref="Requisicion"/> y
/// <see cref="OrdenCompra"/>; CxP, Recepción y Tesorería se incorporan
/// cuando implementen su provider del servicio.
/// </summary>
public enum TipoDocumentoTrazabilidad
{
    Requisicion = 0,
    OrdenCompra = 1,
    // Reservado para implementaciones futuras:
    Recepcion = 2,
    FacturaProveedor = 3,
    PagoProveedor = 4,
}
