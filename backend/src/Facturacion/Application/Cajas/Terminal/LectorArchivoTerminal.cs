using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Millet.Facturacion.Domain.Cajas.Terminal;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cajas.Terminal;

/// <summary>
/// Lee el archivo de la terminal de tarjetas. Acepta dos layouts (ver
/// <see cref="TerminalTarjetaOptions"/>) y usa el primero cuyos encabezados
/// encuentre: el OneGMR TRST de Getnet (CSV, fechas ISO, una fila por transacción
/// liquidada, renglón final <c>ENDOFFILE_…</c>) o el «Transacciones por día» del
/// portal (tabla HTML con extensión <c>.xls</c>; fecha día/mes y hora, el año sale
/// de la fecha del archivo que indica el usuario).
/// </summary>
public static partial class LectorArchivoTerminal
{
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    public static IReadOnlyList<RenglonTerminal> Leer(byte[] contenido, DateOnly fechaArchivo, TerminalTarjetaOptions opciones)
    {
        var texto = Decodificar(contenido);
        var filas = texto.Contains("<tr", StringComparison.OrdinalIgnoreCase) ? FilasHtml(texto) : FilasCsv(texto);

        var perfiles = new[] { (Columnas: opciones.ColumnasGmr, Gmr: true), (Columnas: opciones.Columnas, Gmr: false) };
        foreach (var (c, gmr) in perfiles)
        {
            string[] obligatorias = [c.NoOperacion, c.Fecha, c.Importe, c.Estatus, c.Autorizacion];
            var encabezado = filas.FindIndex(f => obligatorias.All(o => f.Contains(o, StringComparer.OrdinalIgnoreCase)));
            if (encabezado >= 0) return Convertir(filas, encabezado, c, gmr, fechaArchivo, opciones);
        }
        var esperadas = new[] { opciones.Columnas, opciones.ColumnasGmr }
            .Select(c => string.Join(", ", c.NoOperacion, c.Fecha, c.Importe, c.Estatus, c.Autorizacion));
        throw new BusinessRuleException("TERMINAL_ENCABEZADOS",
            $"El archivo no trae las columnas esperadas: {string.Join(" o bien ", esperadas)}.");
    }

    private static List<RenglonTerminal> Convertir(List<List<string>> filas, int encabezado, ColumnasTerminal c, bool gmr,
        DateOnly fechaArchivo, TerminalTarjetaOptions opciones)
    {
        var indice = filas[encabezado]
            .Select((nombre, i) => (nombre, i))
            .GroupBy(x => x.nombre, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().i, StringComparer.OrdinalIgnoreCase);

        string? Valor(List<string> fila, string? columna)
        {
            if (columna is null || !indice.TryGetValue(columna, out var i) || i >= fila.Count) return null;
            // El GMR antepone «'» a algunos números (ARN, RRN) para que Excel no los recorte.
            var v = fila[i].Trim().TrimStart('\'');
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }

        var renglones = new List<RenglonTerminal>();
        for (var r = encabezado + 1; r < filas.Count; r++)
        {
            var fila = filas[r];
            // Las tablas de totales, el renglón ENDOFFILE del GMR o un encabezado repetido no son ventas.
            var operacion = Valor(fila, c.NoOperacion);
            if (operacion is null || string.Equals(operacion, c.NoOperacion, StringComparison.OrdinalIgnoreCase)
                || operacion.StartsWith("ENDOFFILE", StringComparison.OrdinalIgnoreCase)) continue;
            var tipo = Valor(fila, c.TipoMovimiento);
            if (c.TipoMovimiento is not null && tipo is not null
                && !string.Equals(tipo, opciones.TipoMovimientoVenta, StringComparison.OrdinalIgnoreCase)) continue;
            var importe = Monto(Valor(fila, c.Importe))
                ?? throw new BusinessRuleException("TERMINAL_IMPORTE", $"La operación {operacion} no trae un importe válido.");
            var estatus = Valor(fila, c.Estatus);
            var aprobada = gmr
                ? opciones.EstatusAprobadosGmr.Contains(estatus ?? "", StringComparer.OrdinalIgnoreCase)
                : string.Equals(estatus, opciones.EstatusAprobada, StringComparison.OrdinalIgnoreCase);
            renglones.Add(new RenglonTerminal(
                operacion,
                Fecha(Valor(fila, c.Fecha), Valor(fila, c.Hora), fechaArchivo, operacion),
                importe,
                Moneda(Valor(fila, c.Moneda)),
                Ultimos4(Valor(fila, c.Ultimos4)),
                Valor(fila, c.Autorizacion),
                estatus,
                aprobada,
                Monto(Valor(fila, c.Comision)),
                Monto(Valor(fila, c.IvaComision)),
                Monto(Valor(fila, c.Neto)),
                Valor(fila, c.FolioLiquidacion),
                FechaIso(Valor(fila, c.FechaLiquidacion))));
        }
        return renglones;
    }

    /// <summary>ISO 4217 numérico del GMR (484, 840) → alfabético; lo demás se deja igual.</summary>
    private static string? Moneda(string? valor) => valor switch
    {
        "484" => "MXN",
        "840" => "USD",
        _ => valor,
    };

    /// <summary>Tarjeta enmascarada del GMR («1234-56XX-XXXX-0987») → «0987»; 4 dígitos se dejan igual.</summary>
    private static string? Ultimos4(string? valor)
    {
        if (valor is null) return null;
        var digitos = new string(valor.Where(char.IsDigit).ToArray());
        return digitos.Length >= 4 ? digitos[^4..] : null;
    }

    private static DateOnly? FechaIso(string? valor) =>
        DateOnly.TryParseExact(valor?.Split(' ')[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;

    private static string Decodificar(byte[] contenido)
    {
        // Getnet exporta en Latin-1 sin declarar charset; si es UTF-8 válido se respeta.
        try { return new UTF8Encoding(false, true).GetString(contenido); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(contenido); }
    }

    private static List<List<string>> FilasHtml(string html) =>
        Fila().Matches(html)
            .Select(m => Celda().Matches(m.Groups[1].Value)
                .Select(c => WebUtility.HtmlDecode(Etiqueta().Replace(c.Groups[1].Value, "")).Trim())
                .ToList())
            .Where(f => f.Count > 0)
            .ToList();

    private static List<List<string>> FilasCsv(string csv)
    {
        var lineas = csv.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        var separador = lineas.Length > 0 && lineas[0].Count(ch => ch == ';') > lineas[0].Count(ch => ch == ',') ? ';' : ',';
        return lineas.Select(l => PartirCsv(l, separador)).ToList();
    }

    private static List<string> PartirCsv(string linea, char separador)
    {
        var campos = new List<string>();
        var actual = new StringBuilder();
        var comillas = false;
        for (var i = 0; i < linea.Length; i++)
        {
            var ch = linea[i];
            if (ch == '"')
            {
                if (comillas && i + 1 < linea.Length && linea[i + 1] == '"') { actual.Append('"'); i++; }
                else comillas = !comillas;
            }
            else if (ch == separador && !comillas) { campos.Add(actual.ToString().Trim()); actual.Clear(); }
            else actual.Append(ch);
        }
        campos.Add(actual.ToString().Trim());
        return campos;
    }

    /// <summary>«$ 1,234.50» → 1234.50; vacío → null.</summary>
    private static decimal? Monto(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var limpio = valor.Replace("$", "").Replace(",", "").Replace(" ", "").Trim();
        return decimal.TryParse(limpio, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>
    /// GMR: «yyyy-MM-dd» con la hora en otra columna («HH:mm:ss») o «yyyy-MM-dd HH:mm:ss».
    /// Portal: «dd/MM HH:mm» (año de la fecha del archivo) o «dd/MM/yyyy HH:mm».
    /// </summary>
    private static DateTime Fecha(string? valor, string? hora, DateOnly fechaArchivo, string operacion)
    {
        if (valor is not null)
        {
            var iso = hora is null ? valor : $"{valor.Split(' ')[0]} {hora}";
            if (DateTime.TryParseExact(iso, ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd"], CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var gmr))
                return gmr;
            if (DateTime.TryParseExact(valor, ["dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy"], Mx,
                    DateTimeStyles.None, out var completa))
                return completa;
            if (DateTime.TryParseExact($"{valor.Split(' ')[0]}/{fechaArchivo.Year} {(valor.Contains(' ') ? valor[(valor.IndexOf(' ') + 1)..] : "00:00")}",
                    "dd/MM/yyyy HH:mm", Mx, DateTimeStyles.None, out var sinAnio))
                return sinAnio;
        }
        throw new BusinessRuleException("TERMINAL_FECHA", $"La operación {operacion} no trae una fecha válida (AAAA-MM-DD o día/mes hora).");
    }

    [GeneratedRegex(@"<tr[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Fila();

    [GeneratedRegex(@"<t[hd][^>]*>(.*?)</t[hd]>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Celda();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Etiqueta();
}
