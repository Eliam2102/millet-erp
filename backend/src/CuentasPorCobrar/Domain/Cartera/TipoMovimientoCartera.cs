namespace Millet.CuentasPorCobrar.Domain.Cartera;

/// <summary>
/// Tipo de movimiento aplicado a una <see cref="FacturaCartera"/>
/// (CXC-PR3). Persistido como <c>short</c> con check constraint.
/// </summary>
public enum TipoMovimientoCartera : short
{
    /// <summary>Pago aplicado (REPP timbrado o cobro de mostrador).</summary>
    Pago = 1,

    /// <summary>Nota de crédito timbrada relacionada a la factura.</summary>
    NotaCredito = 2,

    /// <summary>
    /// M-240: reversa del ajuste de un cobro corto. Regresa a la factura el faltante que el REP dio por pagado y el ajuste absorbía.
    /// <see cref="MovimientoCartera.OrigenComprobanteId"/> = REP del cobro; resta del pago en la conciliación de cobros contra pólizas.
    /// </summary>
    ReversaAjusteCobro = 3,
}
