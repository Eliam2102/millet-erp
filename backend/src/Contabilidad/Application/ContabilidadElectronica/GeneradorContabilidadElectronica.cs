using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Application.ContabilidadElectronica;

// K10.8 (F1-CON-13): contabilidad electrónica del anexo 24, versión 1.3. Catálogo de cuentas con código agrupador (CT), balanza
// mensual normal o complementaria (BN/BC) y pólizas del periodo con sus CFDI (PL). Funciones puras: el handler lee la base y aquí se
// arma y valida el XML. No se sella: el envío al buzón tributario lo firma Millet con su e.firma.

public enum TipoArchivoContabilidadElectronica { Catalogo, Balanza, Polizas }

/// <summary>N = normal; C = complementaria (lleva fecha de la última modificación).</summary>
public enum TipoEnvioBalanza { Normal, Complementaria }

/// <summary>AF acto de fiscalización, FC fiscalización compulsa (llevan número de orden); DE devolución, CO compensación (llevan número de trámite).</summary>
public enum TipoSolicitudPolizas { AF, FC, DE, CO }

/// <summary>Cuenta que impide generar el catálogo (K10.8-a).</summary>
public sealed record ProblemaCuentaCe(Guid CuentaId, string Codigo, string Nombre, string? CodigoAgrupador, string Problema);

public sealed record FilaBalanzaCe(string Codigo, decimal SaldoIni, decimal Debe, decimal Haber, decimal SaldoFin);

public sealed record PartidaCe(string CuentaCodigo, string CuentaNombre, string? Concepto, decimal Cargo, decimal Abono);

public sealed record PolizaCe(string Folio, DateOnly Fecha, string Concepto, string? Uuid, IReadOnlyList<PartidaCe> Partidas);

/// <summary>Datos del CFDI para el nodo <c>CompNal</c>: RFC de la contraparte (emisor si es recibido, receptor si es emitido).</summary>
public sealed record ComprobanteNacionalContable(string Uuid, string Rfc, decimal MontoTotal, string Moneda, decimal? TipoCambio);

public static partial class GeneradorContabilidadElectronica
{
    public const string SinCodigoAgrupador = "Sin código agrupador SAT";
    public const string CodigoAgrupadorInvalido = "El código agrupador no está en el catálogo del SAT";
    public const string SinNaturaleza = "Sin naturaleza (deudora o acreedora)";

    private const string Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    [GeneratedRegex("^[A-ZÑ&]{3,4}[0-9]{2}[0-1][0-9][0-3][0-9][A-Z0-9]?[A-Z0-9]?[0-9A-Z]?$", RegexOptions.CultureInvariant)]
    private static partial Regex RfcRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacios();

    public static bool EsRfcValido(string? rfc) => rfc is { Length: >= 12 and <= 13 } && RfcRegex().IsMatch(rfc);

    /// <summary>Nombre que pide el SAT: RFC + año + mes + tipo (CT, BN, BC o PL).</summary>
    public static string NombreArchivo(string rfc, int anio, int mes, string sufijo) =>
        $"{rfc}{anio:D4}{mes:D2}{sufijo}";

    // ─── Catálogo ────────────────────────────────────────────────────────────

    /// <summary>Cuentas que van al catálogo: las cuentas activas (los rubros de reporte no son cuentas).</summary>
    public static IReadOnlyList<CuentaContable> CuentasCatalogo(IEnumerable<CuentaContable> cuentas) =>
        [.. cuentas.Where(c => !c.EsRubro && c.Activa).OrderBy(c => c.Codigo, StringComparer.Ordinal)];

    /// <summary>K10.8-a: cuentas del catálogo sin código agrupador válido o sin naturaleza. Si hay alguna, no se genera.</summary>
    public static IReadOnlyList<ProblemaCuentaCe> Revisar(IEnumerable<CuentaContable> cuentas)
    {
        var problemas = new List<ProblemaCuentaCe>();
        foreach (var c in CuentasCatalogo(cuentas))
        {
            if (string.IsNullOrWhiteSpace(c.CodigoAgrupador))
                problemas.Add(new(c.Id, c.Codigo, c.Nombre, null, SinCodigoAgrupador));
            else if (!EsquemasSat.EsCodigoAgrupadorValido(c.CodigoAgrupador))
                problemas.Add(new(c.Id, c.Codigo, c.Nombre, c.CodigoAgrupador, CodigoAgrupadorInvalido));
            if (c.Naturaleza is null)
                problemas.Add(new(c.Id, c.Codigo, c.Nombre, c.CodigoAgrupador, SinNaturaleza));
        }
        return problemas;
    }

    public static XDocument Catalogo(string rfc, int anio, int mes, IEnumerable<CuentaContable> cuentas)
    {
        var todas = cuentas.ToList();
        var porId = todas.ToDictionary(c => c.Id);
        XNamespace ns = EsquemasSat.NsCatalogo;
        var raiz = new XElement(ns + "Catalogo",
            new XAttribute(XNamespace.Xmlns + "catalogocuentas", ns),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
            new XAttribute(XName.Get("schemaLocation", Xsi), EsquemasSat.UbicacionCatalogo),
            new XAttribute("Version", EsquemasSat.Version), new XAttribute("RFC", rfc),
            new XAttribute("Mes", mes.ToString("D2", CultureInfo.InvariantCulture)), new XAttribute("Anio", anio));
        foreach (var c in CuentasCatalogo(todas))
        {
            var cta = new XElement(ns + "Ctas",
                new XAttribute("CodAgrup", c.CodigoAgrupador!.Trim()),
                new XAttribute("NumCta", c.Codigo),
                new XAttribute("Desc", Texto(c.Nombre, 400, c.Codigo)));
            if (c.PadreId is { } padre && porId.TryGetValue(padre, out var p) && !p.EsRubro)
                cta.Add(new XAttribute("SubCtaDe", p.Codigo));
            cta.Add(new XAttribute("Nivel", Math.Max((int)c.Nivel, 1)),
                new XAttribute("Natur", c.Naturaleza == NaturalezaCuenta.Acreedora ? "A" : "D"));
            raiz.Add(cta);
        }
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), raiz);
    }

    // ─── Balanza ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Filas de la balanza: cada cuenta acumula sus descendientes (las de mayor llevan la suma de sus subcuentas, como en el
    /// catálogo). Saldos según la naturaleza de la cuenta (acreedora = abonos − cargos). Solo cuentas con saldo o movimientos.
    /// </summary>
    /// <param name="iniciales">Saldo inicial por cuenta con signo deudor (cargo − abono).</param>
    /// <param name="periodo">Cargos y abonos del mes por cuenta.</param>
    public static IReadOnlyList<FilaBalanzaCe> FilasBalanza(
        IReadOnlyList<CuentaContable> cuentas, IReadOnlyDictionary<Guid, decimal> iniciales,
        IReadOnlyDictionary<Guid, (decimal Cargos, decimal Abonos)> periodo)
    {
        var porId = cuentas.ToDictionary(c => c.Id);
        var acumulado = new Dictionary<Guid, (decimal Inicial, decimal Cargos, decimal Abonos)>();
        foreach (var id in iniciales.Keys.Union(periodo.Keys))
        {
            var inicial = iniciales.GetValueOrDefault(id);
            var (cargos, abonos) = periodo.GetValueOrDefault(id);
            var visitadas = new HashSet<Guid>();
            for (Guid? actual = id; actual is { } a && visitadas.Add(a); actual = porId.GetValueOrDefault(a)?.PadreId)
            {
                var x = acumulado.GetValueOrDefault(a);
                acumulado[a] = (x.Inicial + inicial, x.Cargos + cargos, x.Abonos + abonos);
            }
        }

        return [.. acumulado
            .Where(kv => porId.TryGetValue(kv.Key, out var c) && !c.EsRubro)
            .Select(kv =>
            {
                var c = porId[kv.Key];
                var (inicial, cargos, abonos) = kv.Value;
                var signo = c.Naturaleza == NaturalezaCuenta.Acreedora ? -1m : 1m;
                return new FilaBalanzaCe(c.Codigo, signo * inicial, cargos, abonos, signo * (inicial + cargos - abonos));
            })
            .Where(f => f.SaldoIni != 0m || f.Debe != 0m || f.Haber != 0m || f.SaldoFin != 0m)
            .OrderBy(f => f.Codigo, StringComparer.Ordinal)];
    }

    /// <param name="mes">1 a 12, o 13 para la balanza de cierre (periodo de ajustes).</param>
    public static XDocument Balanza(string rfc, int anio, int mes, TipoEnvioBalanza tipo, DateOnly? fechaModificacion,
        IEnumerable<FilaBalanzaCe> filas)
    {
        XNamespace ns = EsquemasSat.NsBalanza;
        var raiz = new XElement(ns + "Balanza",
            new XAttribute(XNamespace.Xmlns + "BCE", ns),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
            new XAttribute(XName.Get("schemaLocation", Xsi), EsquemasSat.UbicacionBalanza),
            new XAttribute("Version", EsquemasSat.Version), new XAttribute("RFC", rfc),
            new XAttribute("Mes", mes.ToString("D2", CultureInfo.InvariantCulture)), new XAttribute("Anio", anio),
            new XAttribute("TipoEnvio", tipo == TipoEnvioBalanza.Complementaria ? "C" : "N"));
        if (tipo == TipoEnvioBalanza.Complementaria && fechaModificacion is { } f)
            raiz.Add(new XAttribute("FechaModBal", Fecha(f)));
        foreach (var x in filas)
            raiz.Add(new XElement(ns + "Ctas",
                new XAttribute("NumCta", x.Codigo), new XAttribute("SaldoIni", Importe(x.SaldoIni)), new XAttribute("Debe", Importe(x.Debe)),
                new XAttribute("Haber", Importe(x.Haber)), new XAttribute("SaldoFin", Importe(x.SaldoFin))));
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), raiz);
    }

    // ─── Pólizas ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Pólizas del periodo. El CFDI de origen de la póliza (si lo tiene) va como <c>CompNal</c> en cada transacción; si no se
    /// encuentran sus datos (RFC y total) la póliza sale sin comprobante y se avisa.
    /// </summary>
    public static XDocument Polizas(string rfc, int anio, int mes, TipoSolicitudPolizas solicitud, string? numOrden, string? numTramite,
        IEnumerable<PolizaCe> polizas, IReadOnlyDictionary<string, ComprobanteNacionalContable> comprobantes, List<string> avisos)
    {
        XNamespace ns = EsquemasSat.NsPolizas;
        var raiz = new XElement(ns + "Polizas",
            new XAttribute(XNamespace.Xmlns + "PLZ", ns),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
            new XAttribute(XName.Get("schemaLocation", Xsi), EsquemasSat.UbicacionPolizas),
            new XAttribute("Version", EsquemasSat.Version), new XAttribute("RFC", rfc),
            new XAttribute("Mes", mes.ToString("D2", CultureInfo.InvariantCulture)), new XAttribute("Anio", anio),
            new XAttribute("TipoSolicitud", solicitud.ToString()));
        if (solicitud is TipoSolicitudPolizas.AF or TipoSolicitudPolizas.FC && !string.IsNullOrWhiteSpace(numOrden))
            raiz.Add(new XAttribute("NumOrden", numOrden.Trim()));
        if (solicitud is TipoSolicitudPolizas.DE or TipoSolicitudPolizas.CO && !string.IsNullOrWhiteSpace(numTramite))
            raiz.Add(new XAttribute("NumTramite", numTramite.Trim()));

        foreach (var p in polizas)
        {
            var concepto = Texto(p.Concepto, 300, p.Folio);
            var poliza = new XElement(ns + "Poliza",
                new XAttribute("NumUnIdenPol", Texto(p.Folio, 50, "S/N")), new XAttribute("Fecha", Fecha(p.Fecha)),
                new XAttribute("Concepto", concepto));
            XElement? compNal = null;
            if (!string.IsNullOrWhiteSpace(p.Uuid))
            {
                if (comprobantes.TryGetValue(p.Uuid.Trim().ToUpperInvariant(), out var cfdi) && EsRfcValido(cfdi.Rfc))
                    compNal = CompNal(ns, cfdi);
                else
                    avisos.Add($"Póliza {p.Folio}: no se encontró el RFC y el total del CFDI {p.Uuid}; sale sin comprobante.");
            }
            foreach (var x in p.Partidas)
            {
                var transaccion = new XElement(ns + "Transaccion",
                    new XAttribute("NumCta", x.CuentaCodigo), new XAttribute("DesCta", Texto(x.CuentaNombre, 100, x.CuentaCodigo)),
                    new XAttribute("Concepto", Texto(string.IsNullOrWhiteSpace(x.Concepto) ? p.Concepto : x.Concepto, 200, p.Folio)),
                    new XAttribute("Debe", Importe(x.Cargo)), new XAttribute("Haber", Importe(x.Abono)));
                if (compNal is not null) transaccion.Add(new XElement(compNal));
                poliza.Add(transaccion);
            }
            raiz.Add(poliza);
        }
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), raiz);
    }

    private static XElement CompNal(XNamespace ns, ComprobanteNacionalContable c)
    {
        var e = new XElement(ns + "CompNal",
            new XAttribute("UUID_CFDI", c.Uuid.ToUpperInvariant()), new XAttribute("RFC", c.Rfc), new XAttribute("MontoTotal", Importe(c.MontoTotal)));
        var moneda = c.Moneda.Trim().ToUpperInvariant();
        if (moneda is not ("" or Poliza.MonedaNacional))
        {
            e.Add(new XAttribute("Moneda", moneda));
            if (c.TipoCambio is decimal tc && tc > 0m)
                e.Add(new XAttribute("TipCamb", Math.Round(tc, 5, MidpointRounding.AwayFromZero).ToString("0.00###", CultureInfo.InvariantCulture)));
        }
        return e;
    }

    // ─── Formato ─────────────────────────────────────────────────────────────

    internal static string Importe(decimal valor) =>
        Math.Round(valor, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Fecha(DateOnly fecha) => fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Texto en una línea, sin espacios repetidos y recortado al máximo del SAT; vacío = <paramref name="respaldo"/>.</summary>
    internal static string Texto(string? valor, int maximo, string respaldo)
    {
        var limpio = Espacios().Replace(valor ?? string.Empty, " ").Trim();
        if (limpio.Length == 0) limpio = respaldo;
        return limpio.Length <= maximo ? limpio : limpio[..maximo].TrimEnd();
    }
}
