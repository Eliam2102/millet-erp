using Millet.Facturacion.Domain.Comprobantes;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Domain.Repp;

/// <summary>
/// Recibo Electrónico de Pago (REPP) — especialización de <see cref="Comprobante"/>
/// tipo Pago (CFDI tipo P, complemento Pago 2.0; §4.7, §11). Confirma el cobro de
/// una o varias facturas PPD, con parcialidades y diferencia cambiaria automática.
/// El CFDI tipo P no lleva importe en el comprobante (totales en 0); el monto vive
/// en el complemento (las <see cref="ReciboPagoFactura"/>).
/// </summary>
public sealed class ReciboPago : Comprobante
{
    public DateTimeOffset FechaPago { get; private set; }

    /// <summary>Importe total del pago (en la moneda del pago) — informativo de cabecera.</summary>
    public decimal ImporteTotalPago { get; private set; }

    /// <summary>Moneda del pago (puede diferir de la de las facturas cubiertas).</summary>
    public string MonedaPago { get; private set; } = "MXN";

    private readonly List<ReciboPagoFactura> _facturasPagadas = [];
    public IReadOnlyCollection<ReciboPagoFactura> FacturasPagadas => _facturasPagadas.AsReadOnly();

    private ReciboPago() { }

    private ReciboPago(
        Guid id, Guid empresaId, string folio, long folioNumero, Guid sucursalId, Guid? cajaId,
        Guid? usuarioEmisorId, DatosFiscalesReceptor receptor, DatosFiscalesEmisor emisor,
        int periodoAnio, int periodoMes, DateTimeOffset fechaPago, string monedaPago,
        short? canalVentaId)
        : base(id, empresaId, TipoComprobante.Pago, folio, folioNumero, sucursalId, cajaId,
               usuarioEmisorId, receptor, emisor,
               metodoPago: "PUE", formaPago: "01", moneda: "XXX", tipoCambio: null,
               periodoAnio: periodoAnio, periodoMes: periodoMes)
    {
        EstablecerCanalVenta(canalVentaId);
        FechaPago = fechaPago;
        MonedaPago = monedaPago;
        // CFDI tipo P: el comprobante no lleva importe (todo va en el complemento).
        EstablecerTotales(0m, 0m, 0m, 0m, 0m);
    }

    /// <summary>
    /// Crea un REPP en <see cref="EstadoTimbrado.Borrador"/>. El handler agrega las
    /// facturas pagadas y lo timbra. <c>Moneda</c> del comprobante es <c>XXX</c>
    /// (regla del SAT para CFDI tipo P).
    /// </summary>
    public static ReciboPago CrearBorrador(
        Guid empresaId, string folio, long folioNumero, Guid sucursalId, Guid? cajaId,
        Guid? usuarioEmisorId, DatosFiscalesReceptor receptor, DatosFiscalesEmisor emisor,
        int periodoAnio, int periodoMes, DateTimeOffset fechaPago, string monedaPago,
        short? canalVentaId = null)
    {
        return new ReciboPago(
            Guid.CreateVersion7(), empresaId, folio, folioNumero, sucursalId, cajaId, usuarioEmisorId,
            receptor, emisor, periodoAnio, periodoMes, fechaPago, monedaPago, canalVentaId);
    }

    /// <summary>
    /// Agrega una factura cubierta por el pago. Calcula la diferencia cambiaria
    /// (§5, D14): <c>importePagado × (tcPago − tcFactura)</c>; 0 si la factura es
    /// nacional o no hay TC de pago.
    /// </summary>
    public ReciboPagoFactura AgregarFacturaPagada(
        Guid facturaVentaId, string facturaUuid, int numParcialidad, string monedaFactura,
        decimal importePagado, decimal saldoAnterior, string formaPagoReal,
        decimal? tcFactura, decimal? tcPago,
        string? cuentaOrdenante, string? cuentaBeneficiaria, string? referenciaPago,
        string objetoImpDR, decimal facturaTotal,
        IReadOnlyList<ImpuestoFacturaPagada> impuestosFactura)
    {
        if (Estado != EstadoTimbrado.Borrador)
            throw new BusinessRuleException("REPP_INMUTABLE", $"No se pueden agregar pagos a un REPP en estado {Estado}.");
        if (importePagado <= 0)
            throw new BusinessRuleException("REPP_IMPORTE_INVALIDO", "El importe pagado debe ser mayor que cero.");
        if (importePagado > saldoAnterior)
            throw new BusinessRuleException(
                "REPP_SOBREPAGO",
                $"El importe pagado ({importePagado}) excede el saldo anterior ({saldoAnterior}) de la factura.");
        if (string.IsNullOrWhiteSpace(formaPagoReal) || formaPagoReal == "99")
            throw new BusinessRuleException("REPP_FORMA_PAGO_INVALIDA", "El REPP exige la forma de pago real (no 99 Por definir).");

        var gananciaPerdida = CalcularDiferenciaCambiaria(monedaFactura, importePagado, tcFactura, tcPago);

        // EquivalenciaDR: si el pago es en la misma moneda que la factura, 1;
        // si difiere, el TC del pago expresa cuántas unidades de la moneda de la
        // factura equivalen a una del pago.
        var equivalencia = string.Equals(MonedaPago, monedaFactura, StringComparison.OrdinalIgnoreCase)
            ? 1m
            : tcPago ?? 1m;

        var linea = new ReciboPagoFactura(
            Guid.CreateVersion7(), Id, facturaVentaId, facturaUuid, numParcialidad, monedaFactura,
            importePagado, saldoAnterior, saldoAnterior - importePagado, formaPagoReal, tcPago,
            gananciaPerdida, cuentaOrdenante, cuentaBeneficiaria, referenciaPago,
            objetoImpDR, equivalencia, facturaTotal, impuestosFactura);

        _facturasPagadas.Add(linea);
        return linea;
    }

    /// <summary>Fija el importe total del pago (cabecera informativa). Debe llamarse tras agregar las facturas.</summary>
    public void EstablecerImporteTotalPago(decimal importeTotalPago)
    {
        if (Estado != EstadoTimbrado.Borrador)
            throw new BusinessRuleException("REPP_INMUTABLE", $"No se puede modificar un REPP en estado {Estado}.");
        ImporteTotalPago = importeTotalPago;
    }

    /// <summary>
    /// Diferencia cambiaria de un pago en moneda extranjera (D14). Para moneda
    /// nacional (<c>MXN</c>) o sin TCs es 0.
    /// </summary>
    public static decimal CalcularDiferenciaCambiaria(string monedaFactura, decimal importePagado, decimal? tcFactura, decimal? tcPago)
    {
        if (string.Equals(monedaFactura, "MXN", StringComparison.OrdinalIgnoreCase))
            return 0m;
        if (tcFactura is not { } tf || tcPago is not { } tp)
            return 0m;
        return Math.Round(importePagado * (tp - tf), 2);
    }
}
