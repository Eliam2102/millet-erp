using System.Globalization;
using System.Text;

namespace Millet.CuentasPorPagar.Application.Reportes.Diot;

/// <summary>Datos fiscales del tercero para la DIOT. <c>Pais</c> es ISO 3166 alfa-3, como en el catálogo de proveedores.</summary>
public sealed record TerceroDiot(Guid ProveedorId, string Rfc, string Nombre, bool Extranjero, string? Pais, string? IdFiscal);

/// <summary>
/// Una aplicación de pago (o de nota de crédito) sobre un documento del proveedor, con el documento completo
/// para prorratear. Montos en la moneda del documento; <see cref="TipoCambio"/> los lleva a pesos.
/// </summary>
public sealed record MovimientoDiot(
    Guid ProveedorId,
    Guid DocumentoId,
    bool EsDevolucion,
    decimal ImporteAplicado,
    decimal TotalDocumento,
    decimal BaseDocumento,
    decimal IvaDocumento,
    decimal IvaRetenidoDocumento,
    decimal IvaNoAcreditableDocumento,
    decimal TipoCambio,
    string TipoOperacion);

/// <summary>Un renglón de la DIOT: un tercero y un tipo de operación. Importes en pesos con centavos; el TXT los redondea.</summary>
public sealed record RenglonDiot(
    string TipoTercero,
    string TipoOperacion,
    TerceroDiot Tercero,
    decimal ActosRfn8,
    decimal DevolucionesRfn8,
    decimal Actos16,
    decimal Devoluciones16,
    decimal ActosTasa0,
    decimal IvaPagado,
    decimal IvaNoAcreditable,
    decimal IvaRetenido,
    int Documentos);

/// <summary>
/// F1-CXP-10 · Cálculo de la DIOT por flujo de efectivo (pagos del mes), como el papel de trabajo de Contabilidad:
/// el IVA de cada pago es proporcional a lo pagado del documento y la base gravada sale del IVA entre la tasa.
/// </summary>
public static class CalculadoraDiot
{
    public const string TerceroNacional = "04";
    public const string TerceroExtranjero = "05";
    public const string TerceroGlobal = "15";
    public const string OperacionEnajenacion = "02";
    public const string OperacionServicios = "03";
    public const string OperacionArrendamiento = "06";
    public const string OperacionOtros = "85";
    public const string RfcGenerico = "XAXX010101000";

    /// <summary>
    /// Tipo de operación por las claves de producto del documento (supuesto a validar con Contabilidad):
    /// arrendamiento de inmuebles (8013xxxx) = 06; solo servicios (división 70 en adelante) = 03; con algún bien = 02;
    /// sin claves = 85.
    /// </summary>
    public static string TipoOperacionPorClaves(IEnumerable<string?> clavesProdServ)
    {
        var claves = clavesProdServ.Where(c => !string.IsNullOrWhiteSpace(c) && c!.Trim().Length >= 2).Select(c => c!.Trim()).ToList();
        if (claves.Count == 0) return OperacionOtros;
        if (claves.All(c => c.StartsWith("8013", StringComparison.Ordinal))) return OperacionArrendamiento;
        return claves.All(c => int.TryParse(c[..2], out var division) && division >= 70) ? OperacionServicios : OperacionEnajenacion;
    }

    public static string TipoTercero(TerceroDiot t) =>
        t.Extranjero ? TerceroExtranjero
        : string.Equals(t.Rfc?.Trim(), RfcGenerico, StringComparison.OrdinalIgnoreCase) ? TerceroGlobal
        : TerceroNacional;

    public static IReadOnlyList<RenglonDiot> Calcular(IEnumerable<MovimientoDiot> movimientos, IReadOnlyDictionary<Guid, TerceroDiot> terceros)
    {
        var renglones = new List<RenglonDiot>();
        foreach (var grupo in movimientos.Where(m => terceros.ContainsKey(m.ProveedorId))
                     .GroupBy(m => (m.ProveedorId, m.TipoOperacion)))
        {
            var tercero = terceros[grupo.Key.ProveedorId];
            decimal rfn = 0, devRfn = 0, a16 = 0, dev16 = 0, a0 = 0, iva = 0, noAcred = 0, ret = 0;
            foreach (var m in grupo)
            {
                var p = Prorratear(m);
                if (m.EsDevolucion)
                {
                    devRfn += p.Base8; dev16 += p.Base16; a0 -= p.Base0;
                    iva -= p.Iva; noAcred -= p.IvaNoAcreditable; ret -= p.IvaRetenido;
                }
                else
                {
                    rfn += p.Base8; a16 += p.Base16; a0 += p.Base0;
                    iva += p.Iva; noAcred += p.IvaNoAcreditable; ret += p.IvaRetenido;
                }
            }
            renglones.Add(new RenglonDiot(TipoTercero(tercero), grupo.Key.TipoOperacion, tercero,
                Centavos(rfn), Centavos(devRfn), Centavos(a16), Centavos(dev16), Centavos(Math.Max(0, a0)),
                Centavos(iva), Centavos(noAcred), Centavos(ret), grupo.Select(m => m.DocumentoId).Distinct().Count()));
        }
        return renglones.OrderBy(r => r.TipoTercero).ThenBy(r => r.Tercero.Rfc, StringComparer.Ordinal).ThenBy(r => r.TipoOperacion).ToList();
    }

    internal readonly record struct Prorrateo(decimal Base8, decimal Base16, decimal Base0, decimal Iva, decimal IvaNoAcreditable, decimal IvaRetenido);

    /// <summary>
    /// Parte del documento que cubre la aplicación, en pesos. La tasa se deduce de IVA ÷ base del documento
    /// (el CFDI recibido no se guarda desglosado por tasa): ~8 % = región fronteriza norte; si no, 16 %.
    /// Lo que la base no explica con IVA va a tasa 0 % (exento no se distingue sin el desglose: supuesto a validar).
    /// </summary>
    internal static Prorrateo Prorratear(MovimientoDiot m)
    {
        if (m.TotalDocumento <= 0 || m.ImporteAplicado == 0) return default;
        var factor = Math.Min(1m, m.ImporteAplicado / m.TotalDocumento) * (m.TipoCambio <= 0 ? 1m : m.TipoCambio);
        var baseTotal = m.BaseDocumento * factor;
        var iva = m.IvaDocumento * factor;
        decimal b8 = 0, b16 = 0;
        if (m.IvaDocumento > 0 && m.BaseDocumento > 0)
        {
            var tasa = m.IvaDocumento / m.BaseDocumento;
            if (Math.Abs(tasa - 0.08m) < 0.01m) b8 = Math.Min(baseTotal, iva / 0.08m);
            else b16 = Math.Min(baseTotal, iva / 0.16m);
        }
        return new Prorrateo(b8, b16, Math.Max(0, baseTotal - b8 - b16), iva,
            m.IvaNoAcreditableDocumento * factor, m.IvaRetenidoDocumento * factor);
    }

    private static decimal Centavos(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>Pesos enteros, como el archivo que presenta Contabilidad.</summary>
    public static long Pesos(decimal v) => (long)Math.Round(v, 0, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Archivo de carga masiva de la DIOT (layout 2025 del SAT, 54 campos separados por «|», un renglón por tercero y
/// tipo de operación, CRLF). Se llenan los mismos campos que el archivo que Contabilidad presentó en julio-2026:
/// 1-7 identificación; 8/9 región fronteriza norte; 12/13 tasa 16 %; 14 importación; 48 IVA retenido; 50 exentos;
/// 51 tasa 0 %; 54 manifestación «01». Los bloques de IVA acreditable y no acreditable (18 a 47) van vacíos, igual
/// que en ese archivo (supuesto a validar: dónde va el IVA no acreditable en el layout).
/// </summary>
public static class LayoutDiot
{
    public const int Campos = 54;

    public static string Renglon(RenglonDiot r)
    {
        var c = new string[Campos + 1];
        var extranjero = r.TipoTercero == CalculadoraDiot.TerceroExtranjero;
        c[1] = r.TipoTercero;
        c[2] = r.TipoOperacion;
        c[3] = extranjero ? "" : Limpio(r.Tercero.Rfc).ToUpperInvariant();
        c[4] = extranjero ? Limpio(r.Tercero.IdFiscal) : "";
        c[5] = extranjero ? Limpio(r.Tercero.Nombre) : "";
        c[6] = extranjero ? Limpio(r.Tercero.Pais).ToUpperInvariant() : "";
        c[8] = Monto(r.ActosRfn8);
        c[9] = Monto(r.DevolucionesRfn8);
        c[12] = Monto(r.Actos16);
        c[13] = Monto(r.Devoluciones16);
        c[14] = "0";
        c[48] = Entero(r.IvaRetenido);
        c[50] = "0";
        c[51] = Entero(r.ActosTasa0);
        c[54] = "01";
        return string.Join('|', c.Skip(1).Select(v => v ?? ""));
    }

    public static string Archivo(IEnumerable<RenglonDiot> renglones)
    {
        var sb = new StringBuilder();
        foreach (var r in renglones.Where(TieneMontos)) sb.Append(Renglon(r)).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>Un renglón sin actos ni retención no se declara.</summary>
    public static bool TieneMontos(RenglonDiot r) =>
        CalculadoraDiot.Pesos(r.ActosRfn8) != 0 || CalculadoraDiot.Pesos(r.Actos16) != 0 || CalculadoraDiot.Pesos(r.ActosTasa0) != 0
        || CalculadoraDiot.Pesos(r.DevolucionesRfn8) != 0 || CalculadoraDiot.Pesos(r.Devoluciones16) != 0 || CalculadoraDiot.Pesos(r.IvaRetenido) != 0;

    private static string Monto(decimal v) => CalculadoraDiot.Pesos(v) == 0 ? "" : Entero(v);
    private static string Entero(decimal v) => CalculadoraDiot.Pesos(v).ToString(CultureInfo.InvariantCulture);
    private static string Limpio(string? s) => (s ?? "").Replace('|', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
