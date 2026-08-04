namespace Millet.Facturacion.Domain.Repp;

/// <summary>
/// Estructura de impuestos de la factura pagada, tal cual se conoce al emitir el
/// REPP (agrupada por impuesto + tipo factor + tasa). <see cref="BaseGravable"/>
/// es la base TOTAL de la factura para ese grupo; <see cref="ReciboPagoFactura"/>
/// la prorratea al importe pagado para armar el ImpuestosDR.
/// </summary>
public sealed record ImpuestoFacturaPagada(
    string Impuesto,
    string TipoFactor,
    decimal TasaOCuota,
    bool EsRetencion,
    decimal BaseGravable);

/// <summary>
/// Documento relacionado de un <see cref="ReciboPago"/> (nodo <c>DoctoRelacionado</c>
/// del complemento Pago 2.0; §4.7, §11 levantamiento). Una factura PPD cubierta
/// por el pago, con su parcialidad, saldos y la diferencia cambiaria calculada.
/// Entidad hija del agregado <see cref="ReciboPago"/>.
/// </summary>
public sealed class ReciboPagoFactura
{
    public Guid Id { get; private set; }
    public Guid ReciboPagoId { get; private set; }

    /// <summary>Factura de venta cubierta (interno).</summary>
    public Guid FacturaVentaId { get; private set; }

    /// <summary>UUID de la factura cubierta (va en el XML del complemento).</summary>
    public string FacturaUuid { get; private set; } = string.Empty;

    public int NumParcialidad { get; private set; }

    /// <summary>Moneda de la factura cubierta (puede diferir de la del pago).</summary>
    public string MonedaFactura { get; private set; } = "MXN";

    public decimal ImportePagado { get; private set; }
    public decimal SaldoAnterior { get; private set; }
    public decimal SaldoInsoluto { get; private set; }

    /// <summary>Forma de pago real (catálogo SAT, nunca "99 Por definir").</summary>
    public string FormaPagoReal { get; private set; } = string.Empty;

    /// <summary>Tipo de cambio del pago (vs. el de la factura), si aplica moneda extranjera.</summary>
    public decimal? TcPago { get; private set; }

    /// <summary>
    /// Diferencia cambiaria del pago: <c>importePagado × (tcPago − tcFactura)</c>.
    /// Positiva = ganancia, negativa = pérdida (D14). 0 en moneda nacional.
    /// </summary>
    public decimal GananciaPerdidaCambiaria { get; private set; }

    // ---- Datos bancarios (§4.7) ----
    public string? CuentaOrdenante { get; private set; }
    public string? CuentaBeneficiaria { get; private set; }
    public string? ReferenciaPago { get; private set; }

    // ---- Impuestos del documento relacionado (ImpuestosDR, Pago 2.0) ----

    /// <summary>c_ObjetoImp del DoctoRelacionado: 01 No objeto, 02 Sí objeto (con <see cref="Impuestos"/>), 03 Sí objeto no obligado.</summary>
    public string ObjetoImpDR { get; private set; } = "01";

    /// <summary>EquivalenciaDR: unidades de la moneda de la factura por 1 unidad de la moneda del pago (1 si es la misma).</summary>
    public decimal Equivalencia { get; private set; } = 1m;

    /// <summary>Suma de las bases gravadas por traslado (Subtotal del DoctoRelacionado).</summary>
    public decimal BaseGravablePagada { get; private set; }

    private readonly List<ReciboPagoFacturaImpuesto> _impuestos = [];
    public IReadOnlyCollection<ReciboPagoFacturaImpuesto> Impuestos => _impuestos.AsReadOnly();

    private ReciboPagoFactura() { }

    internal ReciboPagoFactura(
        Guid id, Guid reciboPagoId, Guid facturaVentaId, string facturaUuid, int numParcialidad,
        string monedaFactura, decimal importePagado, decimal saldoAnterior, decimal saldoInsoluto,
        string formaPagoReal, decimal? tcPago, decimal gananciaPerdidaCambiaria,
        string? cuentaOrdenante, string? cuentaBeneficiaria, string? referenciaPago,
        string objetoImpDR, decimal equivalencia, decimal facturaTotal,
        IReadOnlyList<ImpuestoFacturaPagada> impuestosFactura)
    {
        Id = id;
        ReciboPagoId = reciboPagoId;
        FacturaVentaId = facturaVentaId;
        FacturaUuid = facturaUuid;
        NumParcialidad = numParcialidad;
        MonedaFactura = monedaFactura;
        ImportePagado = importePagado;
        SaldoAnterior = saldoAnterior;
        SaldoInsoluto = saldoInsoluto;
        FormaPagoReal = formaPagoReal;
        TcPago = tcPago;
        GananciaPerdidaCambiaria = gananciaPerdidaCambiaria;
        CuentaOrdenante = cuentaOrdenante;
        CuentaBeneficiaria = cuentaBeneficiaria;
        ReferenciaPago = referenciaPago;
        ObjetoImpDR = objetoImpDR;
        Equivalencia = equivalencia;

        // Prorrateo del ImpuestosDR: la base de cada impuesto de la factura se
        // lleva a la fracción cubierta por este pago (importePagado / total).
        // 6 decimales (permitido en Pago 2.0; el ejemplo oficial usa esa escala).
        var factor = facturaTotal <= 0m ? 0m : importePagado / facturaTotal;
        foreach (var imp in impuestosFactura)
        {
            var baseDR = Math.Round(imp.BaseGravable * factor, 6, MidpointRounding.AwayFromZero);
            var importeDR = Math.Round(baseDR * imp.TasaOCuota, 6, MidpointRounding.AwayFromZero);
            _impuestos.Add(new ReciboPagoFacturaImpuesto(
                Guid.CreateVersion7(), Id, imp.Impuesto, imp.TipoFactor, imp.TasaOCuota,
                imp.EsRetencion, baseDR, importeDR));
        }

        BaseGravablePagada = _impuestos.Where(i => !i.EsRetencion).Sum(i => i.BaseDR);
    }
}
