namespace Millet.Facturacion.Domain.Pedidos;

/// <summary>
/// Clase de un pedido de origen <see cref="OrigenPedido.Manual"/> (R2 / F6.1).
/// Un pedido manual sólo existe para ventas que <b>no</b> nacen en A+W: la venta
/// directa del ERP (activo, almacenable o servicio) o la captura de emergencia
/// cuando A+W no está disponible. No hay clase «producción»: lo que se produce
/// se factura desde el pedido A+W (F6.1-b).
///
/// <para>Ambas clases se facturan con la serie dedicada
/// <c>TipoDocumentoSerie.VentaDirecta</c> de la sucursal emisora y no se
/// informan de vuelta a A+W (sin write-back).</para>
///
/// <para>El valor numérico (<c>short</c>) está fijo por ABI — agregar al final.</para>
/// </summary>
public enum ClasePedidoManual : short
{
    /// <summary>Venta directa del ERP (F6.1); exige <see cref="SubtipoVentaDirecta"/>.</summary>
    VentaDirecta = 1,

    /// <summary>
    /// Captura de emergencia (A+W fuera de servicio); exige motivo y no lleva
    /// subtipo. Requiere el permiso <c>facturacion.pedidos.capturar-emergencia</c>.
    /// </summary>
    Emergencia = 2,
}
