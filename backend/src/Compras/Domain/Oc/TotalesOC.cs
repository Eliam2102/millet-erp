namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Value object inmutable que representa los totales **computed** de una
/// <see cref="OrdenCompra"/> (diseño §4.9). No se almacena en BD —
/// <see cref="OrdenCompra.CalcularTotales"/> lo recalcula desde las
/// líneas + cabecera (descuento global, gastos adicionales, redondeo)
/// en cada lectura.
///
/// <para>
/// **Fórmula**:
/// <code>
/// SubtotalAntesDescuento = SUM(linea.SubtotalLinea)        ← post-descuento línea
/// DescuentoGlobalAplicado = DescuentoGlobal.Aplicar(SubtotalAntesDescuento)
/// BaseGravable           = SubtotalAntesDescuento - DescuentoGlobalAplicado + GastosAdicionales
/// IvaTotal               = SUM(linea.IvaImporte)
/// RetencionIsrTotal      = SUM(linea.RetencionIsr ?? 0)
/// TotalAPagar            = BaseGravable + IvaTotal - RetencionIsrTotal + Redondeo
/// </code>
/// </para>
///
/// <para>
/// Todos los campos son <see cref="decimal"/> simples sin <c>Money</c>.
/// La moneda viva en <see cref="OrdenCompra.Moneda"/>; el caller puede
/// envolver en <c>Money</c> al exponer al cliente si lo necesita. Esta
/// decisión simplifica la persistencia (no necesitamos splittear cada
/// total en monto+moneda) y centraliza la moneda en la cabecera.
/// </para>
/// </summary>
public readonly record struct TotalesOC(
    decimal SubtotalAntesDescuento,
    decimal DescuentoGlobalAplicado,
    decimal GastosAdicionales,
    decimal BaseGravable,
    decimal IvaTotal,
    decimal RetencionIsrTotal,
    decimal Redondeo,
    decimal TotalAPagar);
