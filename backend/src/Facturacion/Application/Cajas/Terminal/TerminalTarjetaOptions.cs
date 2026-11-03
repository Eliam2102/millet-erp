namespace Millet.Facturacion.Application.Cajas.Terminal;

/// <summary>
/// Mapeo de columnas del archivo de la terminal de tarjetas (86e3vr1tf, 86e3xt2q8).
/// El lector acepta dos layouts y elige el que coincide con los encabezados:
/// <list type="bullet">
/// <item><see cref="Columnas"/>: reporte «Transacciones por día» del portal de Getnet
/// (tabla HTML .xls que envió Millet). No trae comisión ni liquidación.</item>
/// <item><see cref="ColumnasGmr"/>: archivo OneGMR <c>TRST</c> (Transaction Settlement)
/// de Getnet, CSV con coma y punto decimal, nombre
/// <c>GMR_MX_GETNET_&lt;merchant&gt;_YYYYMMDD_P_TRST_SSS.CSV</c> (guía «Getnet One GMR»,
/// junio 2025). Una fila por transacción liquidada con comisión (<c>AMT_TOTAL_FEE</c>),
/// IVA de la comisión (<c>AMT_TOTAL_VAT_TAX</c>), neto (<c>AMT_NET</c> = bruto − comisión − IVA)
/// y lote de liquidación (<c>ID_SETTLEMENT</c>, <c>DAT_SETTLED</c>).</item>
/// </list>
/// Se sobrescribe con <c>Facturacion:TerminalTarjeta:*</c>.
/// </summary>
public sealed class TerminalTarjetaOptions
{
    public const string SectionName = "Facturacion:TerminalTarjeta";

    public ColumnasTerminal Columnas { get; set; } = new();

    public ColumnasTerminal ColumnasGmr { get; set; } = ColumnasTerminal.GmrTrst();

    /// <summary>Valor de la columna de estatus que indica venta aprobada (portal).</summary>
    public string EstatusAprobada { get; set; } = "Aprobada";

    /// <summary>
    /// Valores de <c>DES_TRANSACTION_STATUS</c> del GMR que cuentan como venta aprobada
    /// (glosario de la guía). En el TRST la transacción liquidada llega como «Captured».
    /// </summary>
    public string[] EstatusAprobadosGmr { get; set; } = ["Captured", "Approved", "Authorized"];

    /// <summary>
    /// Valor de <c>DES_FINANCIAL_MOVEMENTS_TYPE</c> de una venta. Los demás movimientos
    /// (Chargeback, Adjustment, Discount, Billing) no son ventas y no se cargan aquí.
    /// </summary>
    public string TipoMovimientoVenta { get; set; } = "Transaction";
}

public sealed class ColumnasTerminal
{
    public string NoOperacion { get; set; } = "No. Operación";
    public string Fecha { get; set; } = "Fecha";
    /// <summary>Hora en columna aparte (GMR: <c>TIM_TRANSACTION</c>). Vacío = la hora viene en <see cref="Fecha"/>.</summary>
    public string? Hora { get; set; }
    public string Importe { get; set; } = "Importe";
    public string Moneda { get; set; } = "Moneda";
    public string Ultimos4 { get; set; } = "TDC";
    public string Autorizacion { get; set; } = "# de Auth.";
    public string Estatus { get; set; } = "Estatus";
    public string Comision { get; set; } = "Comisión";
    public string IvaComision { get; set; } = "IVA Comisión";
    public string Neto { get; set; } = "Neto";
    public string FolioLiquidacion { get; set; } = "Folio de liquidación";
    /// <summary>Fecha en que el banco liquidó (GMR: <c>DAT_SETTLED</c>). Vacío = la del día de la venta.</summary>
    public string? FechaLiquidacion { get; set; }
    /// <summary>Tipo de movimiento financiero (GMR: <c>DES_FINANCIAL_MOVEMENTS_TYPE</c>). Vacío = todo es venta.</summary>
    public string? TipoMovimiento { get; set; }

    /// <summary>Nombres reales del archivo OneGMR TRST de Getnet México.</summary>
    public static ColumnasTerminal GmrTrst() => new()
    {
        NoOperacion = "ID_TRANSACTION",
        Fecha = "DAT_TRANSACTION",
        Hora = "TIM_TRANSACTION",
        Importe = "AMT_GROSS",
        Moneda = "COD_CURRENCY",
        Ultimos4 = "NUM_CARD",
        Autorizacion = "COD_AUTHORIZATION",
        Estatus = "DES_TRANSACTION_STATUS",
        Comision = "AMT_TOTAL_FEE",
        IvaComision = "AMT_TOTAL_VAT_TAX",
        Neto = "AMT_NET",
        FolioLiquidacion = "ID_SETTLEMENT",
        FechaLiquidacion = "DAT_SETTLED",
        TipoMovimiento = "DES_FINANCIAL_MOVEMENTS_TYPE",
    };
}
