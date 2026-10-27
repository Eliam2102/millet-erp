namespace Millet.Facturacion.Domain.Pedidos;

/// <summary>
/// Subtipo de una venta directa del ERP (R2 / F6.1). Sólo aplica a pedidos
/// manuales de clase <see cref="ClasePedidoManual.VentaDirecta"/>.
///
/// <para>El valor numérico (<c>short</c>) está fijo por ABI — agregar al final.</para>
/// </summary>
public enum SubtipoVentaDirecta : short
{
    /// <summary>
    /// Venta de un activo fijo: va siempre con comportamiento fiscal
    /// <c>VentaActivoFijo</c> y exige la autorización del Director de Administración y Finanzas
    /// antes de timbrar (CA6.12).
    /// </summary>
    Activo = 1,

    /// <summary>Venta de un artículo almacenable (inventario no producido en A+W).</summary>
    Almacenable = 2,

    /// <summary>Venta de un servicio.</summary>
    Servicio = 3,
}
