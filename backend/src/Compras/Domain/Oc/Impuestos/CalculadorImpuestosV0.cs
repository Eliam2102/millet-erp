namespace Millet.Compras.Domain.Oc.Impuestos;

/// <summary>
/// Motor de cálculo de impuestos v0 (F2-PR1). Aplica IVA 16% fijo sobre
/// el subtotal post-descuento de cada línea. Sin retenciones ISR.
///
/// <para>
/// **F3-PR2 reemplaza este motor** por el real basado en regímenes
/// fiscales (C3 del diseño): lookup por <c>(regimen_proveedor,
/// regimen_articulo)</c> en <c>compras.regimenes_fiscales_articulo</c>
/// para determinar IVA aplicable + retención ISR si aplica. Mientras
/// tanto, v0 cubre el 95% de los casos comunes (régimen general MX).
/// </para>
///
/// <para>
/// Redondeo: 2 decimales <see cref="MidpointRounding.ToEven"/> (banker's
/// rounding) — minimiza sesgo en agregaciones grandes.
/// </para>
/// </summary>
public static class CalculadorImpuestosV0
{
    public const decimal TasaIvaGeneral = 0.16m;

    /// <summary>
    /// Calcula el IVA de una línea sobre su subtotal post-descuento.
    /// Devuelve 0 si el subtotal es ≤ 0.
    /// </summary>
    public static decimal CalcularIvaLinea(decimal subtotalPostDescuento)
    {
        if (subtotalPostDescuento <= 0m) return 0m;
        return Math.Round(subtotalPostDescuento * TasaIvaGeneral, 2, MidpointRounding.ToEven);
    }

    /// <summary>
    /// Retención ISR. Motor v0 no aplica retenciones — siempre devuelve
    /// <c>null</c>. F3-PR2 lo reemplaza con lookup por régimen.
    /// </summary>
    public static decimal? CalcularRetencionIsrLinea(decimal subtotalPostDescuento)
    {
        _ = subtotalPostDescuento;
        return null;
    }
}
