namespace Millet.CuentasPorPagar.Domain.TarjetaCredito;

/// <summary>
/// Naturaleza del cargo en una TC empresarial (§3.2 del anexo TC).
/// F7-PR4 implementa <see cref="CompraConCfdi"/> y
/// <see cref="CompraSinCfdi"/> con factory methods completos. Los demás
/// tipos (Refund, GastoFinanciero, Anualidad, ComisionDivisa) están
/// reservados en el enum y se capturan vía F7-PR5 (conciliación con
/// estado de cuenta del banco) y F7-PR6 (refunds + casos especiales).
/// </summary>
public enum TipoMovimientoTc
{
    /// <summary>Compra con CFDI a nombre de Millet — genera FacturaProveedor (Flujo A §5.1).</summary>
    CompraConCfdi       = 1,

    /// <summary>Compra con solo ticket — solo asiento contable, no genera factura (Flujo B §5.2).</summary>
    CompraSinCfdi       = 2,

    /// <summary>Reembolso del proveedor a la TC (F7-PR5/PR6, Flujo E §5.5).</summary>
    Refund              = 3,

    /// <summary>Intereses moratorios del banco (F7-PR5+).</summary>
    GastoFinanciero     = 4,

    /// <summary>Anualidad de la TC (F7-PR5+).</summary>
    Anualidad           = 5,

    /// <summary>Comisión por uso en divisa extranjera (F7-PR5+).</summary>
    ComisionDivisa      = 6,
}
