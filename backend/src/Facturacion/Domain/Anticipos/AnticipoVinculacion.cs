namespace Millet.Facturacion.Domain.Anticipos;

/// <summary>
/// Vinculación de un <see cref="Anticipo"/> a una factura de venta final
/// (Momento 2, §6.3 levantamiento; relación CFDI tipo 07). Es entidad hija del
/// agregado <see cref="Anticipo"/>: se crea/lee siempre a través de él.
///
/// <para>
/// <b>La vinculación (M2) no reduce el saldo amortizado del anticipo</b> — solo
/// lo compromete. La reducción efectiva ocurre cuando se timbra la NC de
/// amortización (M3, F4-PR2), que rellena <see cref="NcAmortizacionId"/>. Hasta
/// entonces el importe se descuenta del <c>SaldoDisponible</c> (vinculable) pero
/// no del <c>Saldo</c> (amortizado).
/// </para>
/// </summary>
public sealed class AnticipoVinculacion
{
    public Guid Id { get; private set; }
    public Guid AnticipoId { get; private set; }

    /// <summary>Factura de venta final a la que se aplica el anticipo (relación 07).</summary>
    public Guid FacturaVentaId { get; private set; }

    /// <summary>
    /// NC de amortización que materializa la reducción del saldo (M3, F4-PR2).
    /// Null mientras la vinculación está comprometida pero aún no amortizada.
    /// </summary>
    public Guid? NcAmortizacionId { get; private set; }

    /// <summary>Importe del anticipo aplicado a esta factura.</summary>
    public decimal Importe { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    private AnticipoVinculacion() { }

    internal AnticipoVinculacion(Guid id, Guid anticipoId, Guid facturaVentaId, decimal importe, DateTimeOffset creadoEn)
    {
        Id = id;
        AnticipoId = anticipoId;
        FacturaVentaId = facturaVentaId;
        Importe = importe;
        CreadoEn = creadoEn;
    }

    /// <summary>
    /// Asocia la NC de amortización a esta vinculación (M3, F4-PR2). El agregado
    /// <see cref="Anticipo"/> lo invoca al registrar la amortización.
    /// </summary>
    internal void AsociarNotaCredito(Guid ncAmortizacionId) => NcAmortizacionId = ncAmortizacionId;
}
