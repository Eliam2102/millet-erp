namespace Millet.Contabilidad.Application.Motor;

/// <summary>
/// 86e3zcbjx · póliza de la factura mensual del banco por las comisiones de la terminal de tarjetas. Nace cuando Facturación liga el
/// CFDI del banco a los cargos del mes (cuadre con diferencia de hasta $0.01). Cuentas por operación de Contabilidad del 5-nov:
/// comisión 701.10.00.00 y su IVA a 118.01.01.00 (ya pagado: el banco lo cobra en automático) contra la cuenta del banco de la terminal;
/// la diferencia de hasta un centavo va a redondeo 703.21.00.00.
/// <para>
/// Importes: <c>Comision</c> e <c>Iva</c> son los del CFDI (lo que se deduce y se acredita); <c>Total</c> es lo que el banco cargó en
/// el mes; <c>Redondeo</c> = Total − Comision − Iva, con signo (positivo = cargo, negativo = abono: el motor invierte el lado). Así la
/// póliza siempre cuadra contra lo que salió del banco.
/// </para>
/// <para>
/// Fecha contable = último día del mes de los cargos (supuesto a validar: la comisión es gasto del mes en que el banco la cobró, aunque
/// el CFDI se ligue al mes siguiente). Documento = la factura del banco ligada (una póliza por factura: el evento repetido no duplica).
/// </para>
/// </summary>
public static partial class NormalizadorEventos
{
    public const string FacturaBancoTerminalLigada = "facturacion.terminal.factura-banco-ligada.v1";
    public const string TerminalLiquidacionRegistrada = "facturacion.terminal.liquidacion-registrada.v1";

    public static readonly (string Modulo, string Tipo) DocFacturaBancoTerminal = ("facturacion", "factura_banco_terminal");
    public static readonly (string Modulo, string Tipo) DocLiquidacionTerminal = ("facturacion", "liquidacion_terminal");

    private static EventoContable? NormalizarTerminal(string tipo, Guid eventoId, string payload) => tipo switch
    {
        FacturaBancoTerminalLigada => FacturaBancoTerminal(eventoId, payload),
        TerminalLiquidacionRegistrada => LiquidacionTerminal(eventoId, payload),
        _ => null,
    };

    /// <summary>
    /// Póliza de la liquidación de la terminal de tarjetas (una por folio del banco). Contabilidad del 11-nov: la comisión de los cobros
    /// con TPV va a la cuenta 601.75 (rol <c>COMISIONES_TPV</c>); las demás comisiones bancarias siguen en 701.10
    /// (<c>COMISIONES_BANCARIAS</c>). Cargo: banco por el neto, comisión e IVA pendiente; abono: «COBROS T/C, T/D» por el bruto.
    /// <para>
    /// Sucursal (supuesto a validar): la de las ventas liquidadas; si el folio trae ventas de varias sucursales, la de mayor importe
    /// (la póliza lleva una sola sucursal). Fecha contable = el día que el banco liquidó. Un folio al que se agregan ventas después
    /// publica un evento nuevo solo con lo agregado, con el mismo documento y su propia llave: no duplica.
    /// </para>
    /// </summary>
    private static EventoContable LiquidacionTerminal(Guid eventoId, string payload)
    {
        var l = Leer<LiquidacionTerminalContrato>(payload);
        if (l.LiquidacionId == Guid.Empty)
            throw new EventoIlegibleException($"{TerminalLiquidacionRegistrada} sin liquidación.");
        var sucursal = (l.Ventas ?? []).GroupBy(v => v.SucursalId).OrderByDescending(g => g.Sum(v => v.Importe))
            .Select(g => (Guid?)g.Key).FirstOrDefault();
        return new(TerminalLiquidacionRegistrada, eventoId, l.EmpresaId, l.Fecha,
            Doc(DocLiquidacionTerminal, l.LiquidacionId, l.FolioLiquidacion), sucursal, null, null, null, null, "MXN", null,
            new Dictionary<string, decimal?>
            {
                ["Bruto"] = l.Bruto, ["Comision"] = l.Comision, ["IvaComision"] = l.IvaComision, ["Neto"] = l.Neto,
            },
            [], payload);
    }

    /// <summary>Espejo de <c>TerminalLiquidacionRegistradaIntegrationEvent</c> (Facturación), solo lo que usa Contabilidad.</summary>
    private sealed record LiquidacionTerminalContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid LiquidacionId, string? FolioLiquidacion,
        DateOnly Fecha, decimal Bruto, decimal Comision, decimal IvaComision, decimal Neto, IReadOnlyList<VentaLiquidadaContrato>? Ventas);

    private sealed record VentaLiquidadaContrato(Guid SucursalId, decimal Importe);

    private static EventoContable FacturaBancoTerminal(Guid eventoId, string payload)
    {
        var f = Leer<FacturaBancoTerminalContrato>(payload);
        if (f.FacturaBancoId == Guid.Empty || f.Anio is < 2000 or > 2100 || f.Mes is < 1 or > 12)
            throw new EventoIlegibleException($"{FacturaBancoTerminalLigada} sin factura del banco o con mes inválido.");
        // Eventos anteriores a 86e3zcbjx no traen los importes del CFDI: cuadraban al centavo, así que son los de los cargos.
        var comision = f.ComisionFactura ?? f.Comision;
        var iva = f.ComisionFactura is null ? f.IvaComision : f.IvaFactura ?? 0m;
        var fecha = new DateOnly(f.Anio, f.Mes, 1).AddMonths(1).AddDays(-1);
        return new(FacturaBancoTerminalLigada, eventoId, f.EmpresaId, fecha,
            Doc(DocFacturaBancoTerminal, f.FacturaBancoId, $"{f.Anio}-{f.Mes:00}", f.UuidCfdi), f.SucursalId, null, null, null, null,
            "MXN", null,
            new Dictionary<string, decimal?>
            {
                ["Comision"] = comision,
                ["Iva"] = iva,
                ["Redondeo"] = f.Total - comision - iva,
                ["Total"] = f.Total,
            },
            [], payload);
    }

    /// <summary>Espejo de <c>TerminalFacturaBancoLigadaIntegrationEvent</c> (Facturación), solo lo que usa Contabilidad.</summary>
    private sealed record FacturaBancoTerminalContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid FacturaBancoId, string? UuidCfdi,
        int Anio, int Mes, decimal Comision, decimal IvaComision, decimal Total, decimal? ComisionFactura, decimal? IvaFactura,
        Guid? SucursalId);
}
