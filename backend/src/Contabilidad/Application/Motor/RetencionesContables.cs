namespace Millet.Contabilidad.Application.Motor;

/// <summary>
/// 86e3zcbjw · retenciones por concepto en las pólizas automáticas. Cada retención del evento viaja como una línea del grupo
/// <see cref="LineaEventoContable.GrupoRetencion"/> con clasificador <c>{CONCEPTO}_{IMPUESTO}</c> (p. ej. FLETES_IVA, HONORARIOS_PF_ISR);
/// la línea de plantilla con rol por clasificador usa el rol <c>{rol base}_{clasificador}</c> si Contabilidad le dio cuenta y, si no, el
/// rol base. Así cada concepto va a su cuenta (tabla de Contabilidad del 5-nov) sin cambiar la plantilla al agregar cuentas.
/// </summary>
public static class RetencionesContables
{
    /// <summary>Retención que descuenta un pago a proveedor (plantilla #5 v3).</summary>
    public const string CampoAplicada = "RetencionAplicada";

    /// <summary>Total antes de retenciones: proveedor de la factura (#2 v3) y de sus notas (#3b, #3c), y cargo al proveedor del pago (#5 v3).</summary>
    public const string CampoTotalMas = "TotalMasRetenciones";

    public const string RolPorPagar = "RETENCIONES_POR_PAGAR";
    public const string RolClientes = "RETENCIONES_CLIENTES";

    /// <summary>Impuesto SAT (001/002/003 o su nombre) → ISR, IVA o IEPS; null si no se reconoce.</summary>
    public static string? Impuesto(string? codigo) => codigo?.Trim().ToUpperInvariant() switch
    {
        "001" or "ISR" => "ISR",
        "002" or "IVA" => "IVA",
        "003" or "IEPS" => "IEPS",
        _ => null,
    };

    /// <summary><c>{CONCEPTO}_{IMPUESTO}</c>; sin concepto, solo el impuesto (cae al rol base si no hay rol con ese sufijo).</summary>
    public static string Clasificador(string? concepto, string impuesto) =>
        string.IsNullOrWhiteSpace(concepto) ? impuesto : $"{MotorPolizasClasificador(concepto)}_{impuesto}";

    /// <summary>Una línea de grupo retención por clasificador (suma las del mismo concepto e impuesto; omite ceros).</summary>
    public static List<LineaEventoContable> Lineas(IEnumerable<(string? Clasificador, decimal Importe)> retenciones) =>
        [.. retenciones.GroupBy(r => r.Clasificador ?? string.Empty)
            .Select(g => (Clasificador: g.Key, Importe: g.Sum(r => r.Importe)))
            .Where(r => r.Importe != 0m)
            .Select(r => new LineaEventoContable(null, r.Importe, null, false,
                Clasificador: r.Clasificador.Length == 0 ? null : r.Clasificador, Grupo: LineaEventoContable.GrupoRetencion))];

    /// <summary>Líneas de retención de un contrato con concepto por retención o uno para todo el documento.</summary>
    public static List<LineaEventoContable> Lineas(IEnumerable<RetencionContrato>? retenciones, string? conceptoDocumento) =>
        Lineas((retenciones ?? []).Where(r => r.Importe > 0m && Impuesto(r.Impuesto) is not null)
            .Select(r => ((string?)Clasificador(r.Concepto ?? conceptoDocumento, Impuesto(r.Impuesto)!), r.Importe)));

    private static string MotorPolizasClasificador(string concepto) => GenerarPolizaDesdeEventoHandler.Clasificador(concepto);

    /// <summary>Retención en los contratos de CxP y Facturación (<c>Concepto</c> opcional, aditivo).</summary>
    public sealed record RetencionContrato(string? Impuesto, decimal? Tasa, decimal Importe, string? Concepto = null);
}
