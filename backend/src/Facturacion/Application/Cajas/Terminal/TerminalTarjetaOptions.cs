namespace Millet.Facturacion.Application.Cajas.Terminal;

/// <summary>
/// Mapeo de columnas del archivo de la terminal de tarjetas (86e3vr1tf). Por
/// omisión, los encabezados del reporte «Transacciones por día» de Getnet que
/// envió Millet. Comisión, IVA, neto y folio de liquidación son columnas
/// SUPUESTAS: vienen en los reportes «Comisiones Bancarias» / «Reporte
/// conciliación» que aún no recibimos (a validar con Getnet). Se sobrescribe
/// con <c>Facturacion:TerminalTarjeta:Columnas:*</c>.
/// </summary>
public sealed class TerminalTarjetaOptions
{
    public const string SectionName = "Facturacion:TerminalTarjeta";

    public ColumnasTerminal Columnas { get; set; } = new();

    /// <summary>Valor de la columna de estatus que indica venta aprobada.</summary>
    public string EstatusAprobada { get; set; } = "Aprobada";
}

public sealed class ColumnasTerminal
{
    public string NoOperacion { get; set; } = "No. Operación";
    public string Fecha { get; set; } = "Fecha";
    public string Importe { get; set; } = "Importe";
    public string Moneda { get; set; } = "Moneda";
    public string Ultimos4 { get; set; } = "TDC";
    public string Autorizacion { get; set; } = "# de Auth.";
    public string Estatus { get; set; } = "Estatus";
    // Supuestas (a validar con el reporte de comisiones de Getnet).
    public string Comision { get; set; } = "Comisión";
    public string IvaComision { get; set; } = "IVA Comisión";
    public string Neto { get; set; } = "Neto";
    public string FolioLiquidacion { get; set; } = "Folio de liquidación";
}
