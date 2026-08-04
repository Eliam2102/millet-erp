namespace Millet.Facturacion.Domain.Repp;

/// <summary>
/// Impuesto del documento relacionado de un <see cref="ReciboPagoFactura"/>
/// (nodo <c>TrasladoDR</c>/<c>RetencionDR</c> de <c>ImpuestosDR</c>, complemento
/// Pago 2.0; §4.7). Snapshot ya prorrateado al importe pagado: <c>BaseDR</c> es
/// la porción de la base gravable de la factura que corresponde a este pago, en
/// la moneda de la factura, e <c>ImporteDR</c> = BaseDR × TasaOCuota. Entidad
/// nieta del agregado <see cref="ReciboPago"/> (hija de <see cref="ReciboPagoFactura"/>).
/// </summary>
public sealed class ReciboPagoFacturaImpuesto
{
    public Guid Id { get; private set; }
    public Guid ReciboPagoFacturaId { get; private set; }

    /// <summary>c_Impuesto SAT: 001 ISR, 002 IVA, 003 IEPS.</summary>
    public string Impuesto { get; private set; } = "002";

    /// <summary>c_TipoFactor: Tasa | Cuota | Exento.</summary>
    public string TipoFactor { get; private set; } = "Tasa";

    public decimal TasaOCuota { get; private set; }

    /// <summary>True = retención (RetencionDR); false = traslado (TrasladoDR).</summary>
    public bool EsRetencion { get; private set; }

    /// <summary>Base gravable del pago para este impuesto (moneda de la factura).</summary>
    public decimal BaseDR { get; private set; }

    /// <summary>Importe del impuesto sobre el pago = BaseDR × TasaOCuota.</summary>
    public decimal ImporteDR { get; private set; }

    private ReciboPagoFacturaImpuesto() { }

    internal ReciboPagoFacturaImpuesto(
        Guid id, Guid reciboPagoFacturaId, string impuesto, string tipoFactor,
        decimal tasaOCuota, bool esRetencion, decimal baseDR, decimal importeDR)
    {
        Id = id;
        ReciboPagoFacturaId = reciboPagoFacturaId;
        Impuesto = impuesto;
        TipoFactor = tipoFactor;
        TasaOCuota = tasaOCuota;
        EsRetencion = esRetencion;
        BaseDR = baseDR;
        ImporteDR = importeDR;
    }
}
