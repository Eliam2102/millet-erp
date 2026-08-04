namespace Millet.Facturacion.Domain.NotasCredito;

/// <summary>
/// Motivo de una <see cref="NotaCredito"/> (§4.4 levantamiento). Determina la
/// clave SAT de tipo de relación CFDI y si el importe afecta inventario.
/// </summary>
public enum MotivoNotaCredito : short
{
    /// <summary>Aplicación de anticipo contra la factura final (relación 07). No afecta inventario.</summary>
    Amortizacion = 1,

    /// <summary>Descuento comercial posterior a la venta (relación 01). No afecta inventario. F5.</summary>
    Bonificacion = 2,

    /// <summary>Devolución de mercancía (relación 03). Sí afecta inventario. F5+.</summary>
    Devolucion = 3,

    /// <summary>
    /// Descuento "ranura" del pedido A+W (relación 01, RANURA-PR2). Se
    /// autogenera y timbra al timbrar la factura del pedido: la factura va
    /// por el total y esta NC documenta la ranura ([Decisión 13-K]: la caja
    /// cobra total − NC). Motivo propio para distinguirla de las NC
    /// comerciales reales (bonificación/devolución) en listados y reportes.
    /// </summary>
    Ranura = 4,
}

/// <summary>Mapea el motivo a la clave SAT del tipo de relación CFDI (<c>c_TipoRelacion</c>).</summary>
public static class MotivoNotaCreditoExtensions
{
    public static string ClaveTipoRelacion(this MotivoNotaCredito motivo) => motivo switch
    {
        MotivoNotaCredito.Amortizacion => "07",
        MotivoNotaCredito.Bonificacion => "01",
        MotivoNotaCredito.Devolucion => "03",
        MotivoNotaCredito.Ranura => "01",
        _ => "01",
    };
}
