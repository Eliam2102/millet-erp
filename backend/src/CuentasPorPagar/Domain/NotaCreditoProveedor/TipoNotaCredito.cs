namespace Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;

/// <summary>
/// Tipo de NC del proveedor según §4.4 del 00-levantamiento.
/// Determina el comportamiento contable y de aplicación.
/// </summary>
public enum TipoNotaCredito
{
    /// <summary>Descuento sobre factura existente. Relación CFDI tipo 01.</summary>
    Descuento              = 1,

    /// <summary>NC fiscal por devolución de mercancía. Relación CFDI tipo 03. Cierra ciclo con Almacén.</summary>
    Devolucion             = 2,

    /// <summary>NC por amortización de anticipo. Relación CFDI tipo 07.</summary>
    AmortizacionAnticipo   = 3,
}

/// <summary>
/// Tipo de relación CFDI según el catálogo SAT
/// (<c>c_TipoRelacion</c>). Reflejado del XML del CFDI Egreso del
/// proveedor. Define el matching contra factura/anticipo origen.
/// </summary>
public enum TipoRelacionCfdi
{
    /// <summary>01 — Nota de crédito de los documentos relacionados.</summary>
    NotaCredito             = 1,

    /// <summary>03 — Devolución de mercancía sobre facturas o traslados previos.</summary>
    Devolucion              = 3,

    /// <summary>07 — CFDI por aplicación de anticipo.</summary>
    AmortizacionAnticipo    = 7,
}
