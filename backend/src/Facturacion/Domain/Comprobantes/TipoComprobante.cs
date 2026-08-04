namespace Millet.Facturacion.Domain.Comprobantes;

/// <summary>
/// Tipo de comprobante fiscal (c_TipoDeComprobante del SAT). Determina el
/// subtipo de <see cref="Comprobante"/> y su semántica fiscal (§3 levantamiento).
///
/// <para>El valor numérico (<c>short</c>) está fijo por ABI — agregar nuevos al
/// final, nunca renumerar.</para>
/// </summary>
public enum TipoComprobante : short
{
    /// <summary>I — Ingreso (factura de venta, anticipo).</summary>
    Ingreso = 1,

    /// <summary>E — Egreso (nota de crédito).</summary>
    Egreso = 2,

    /// <summary>T — Traslado (Carta Porte de mercancía propia).</summary>
    Traslado = 3,

    /// <summary>P — Pago (REPP, complemento Pago 2.0).</summary>
    Pago = 4,
}
