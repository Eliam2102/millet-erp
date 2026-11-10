namespace Millet.Contabilidad.Application.Motor;

/// <summary>
/// M-209 (86e3zr7mp) · IVA por tratamiento (CON-07-01). CxP y Facturación mandan en sus eventos la base y el IVA de cada tratamiento,
/// armados de los conceptos del CFDI. El IVA viaja como líneas del grupo <see cref="LineaEventoContable.GrupoIva"/> con clasificador =
/// tratamiento; la línea de IVA pendiente o no cobrado de la plantilla, con rol por clasificador, usa <c>{rol base}_{tratamiento}</c> si
/// Contabilidad le dio cuenta y, si no, el rol base (mismo mecanismo que las retenciones, #489). Las bases sin IVA no generan partida,
/// pero quedan en el saldo de IVA del documento para la DIOT. Un evento sin el detalle se trata como hoy: todo al 16 %.
/// </summary>
public static class ImpuestosTrasladados
{
    public const string Tasa16 = "TASA16";
    public const string Tasa8 = "TASA8";
    public const string Tasa0 = "TASA0";
    public const string Exento = "EXENTO";
    public const string NoObjeto = "NOOBJETO";
    public const string NoAcreditable = "NOACREDITABLE";

    public static readonly IReadOnlySet<string> Validos = new HashSet<string>(StringComparer.Ordinal)
        { Tasa16, Tasa8, Tasa0, Exento, NoObjeto, NoAcreditable };

    /// <summary>Tratamiento normalizado (mayúsculas, sin espacios ni guiones bajos); desconocido → null.</summary>
    public static string? Normalizar(string? tratamiento)
    {
        var t = tratamiento?.Trim().ToUpperInvariant().Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return t is not null && Validos.Contains(t) ? t : null;
    }

    /// <summary>
    /// Tratamiento de un traslado de IVA del CFDI: <c>TipoFactor</c> Exento → EXENTO; tasa 0.16 → TASA16; 0.08 → TASA8; 0 → TASA0.
    /// Concepto con <c>ObjetoImp</c> 01 (no objeto) → NOOBJETO. Otra tasa → null (no se adivina).
    /// </summary>
    public static string? DeTraslado(string? objetoImp, string? tipoFactor, decimal? tasa)
    {
        if (string.Equals(objetoImp?.Trim(), "01", StringComparison.Ordinal)) return NoObjeto;
        if (string.Equals(tipoFactor?.Trim(), "Exento", StringComparison.OrdinalIgnoreCase)) return Exento;
        return tasa switch
        {
            0.16m => Tasa16,
            0.08m => Tasa8,
            0m => Tasa0,
            _ => null,
        };
    }

    /// <summary>Base e IVA de un tratamiento en los contratos de CxP y Facturación (aditivo).</summary>
    public sealed record ImpuestoTrasladoContrato(string? Tratamiento, decimal Base, decimal Iva);

    /// <summary>Tratamientos válidos del contrato, agrupados (los desconocidos se ignoran: el IVA del documento manda).</summary>
    public static List<(string Tratamiento, decimal Base, decimal Iva)> Agrupar(IEnumerable<ImpuestoTrasladoContrato>? impuestos) =>
        [.. (impuestos ?? []).Select(i => (Tratamiento: Normalizar(i.Tratamiento), i.Base, i.Iva))
            .Where(i => i.Tratamiento is not null)
            .GroupBy(i => i.Tratamiento!)
            .Select(g => (g.Key, g.Sum(i => i.Base), g.Sum(i => i.Iva)))];

    /// <summary>Una línea del grupo IVA por tratamiento con IVA distinto de cero (con signo, para las notas y reversas).</summary>
    public static List<LineaEventoContable> Lineas(IEnumerable<(string Tratamiento, decimal Iva)> ivas) =>
        [.. ivas.GroupBy(i => i.Tratamiento)
            .Select(g => (Tratamiento: g.Key, Iva: g.Sum(i => i.Iva)))
            .Where(i => i.Iva != 0m)
            .Select(i => new LineaEventoContable(null, i.Iva, null, false, Clasificador: i.Tratamiento, Grupo: LineaEventoContable.GrupoIva))];

    /// <summary>
    /// Líneas de IVA de un documento con detalle. Solo si el detalle cuadra con el IVA del documento (± 0.05 por redondeo, que va al
    /// tratamiento mayor); si no cuadra o no hay detalle, ninguna (la plantilla usa el IVA total en el rol base, como hoy).
    /// </summary>
    public static List<LineaEventoContable> LineasDocumento(IReadOnlyList<(string Tratamiento, decimal Base, decimal Iva)> tratamientos,
        decimal? ivaDocumento)
    {
        if (tratamientos.Count == 0 || ivaDocumento is not { } total) return [];
        var ivas = tratamientos.Where(t => t.Iva != 0m).Select(t => (t.Tratamiento, t.Iva)).ToList();
        var diferencia = total - ivas.Sum(i => i.Iva);
        if (Math.Abs(diferencia) > 0.05m) return [];
        if (diferencia != 0m && ivas.Count > 0)
        {
            var mayor = ivas.IndexOf(ivas.MaxBy(i => Math.Abs(i.Iva)));
            ivas[mayor] = (ivas[mayor].Tratamiento, ivas[mayor].Iva + diferencia);
        }
        return Lineas(ivas);
    }

    /// <summary>Tratamientos que guarda el saldo del documento: los del detalle o, sin él, todo al 16 % con la base gravada.</summary>
    public static List<(string Tratamiento, decimal Base, decimal Iva)> ParaSaldo(
        IReadOnlyList<(string Tratamiento, decimal Base, decimal Iva)> tratamientos, decimal? subtotal, decimal iva) =>
        tratamientos.Count > 0 && Math.Abs(tratamientos.Sum(t => t.Iva) - iva) <= 0.05m
            ? [.. tratamientos]
            : iva != 0m || (subtotal ?? 0m) != 0m ? [(Tasa16, subtotal ?? Math.Round(iva / 0.16m, 2, MidpointRounding.AwayFromZero), iva)] : [];
}
