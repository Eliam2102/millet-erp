using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Millet.Facturacion.Domain.Cajas.Terminal;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cajas.Terminal;

/// <summary>
/// Lee el archivo de la terminal de tarjetas: la exportación de Getnet es una
/// tabla HTML con extensión <c>.xls</c>; también acepta CSV. Toma la primera
/// fila de encabezados que contenga las columnas obligatorias y convierte cada
/// renglón con el mapeo de <see cref="TerminalTarjetaOptions"/>. La fecha viene
/// como día/mes y hora; el año sale de la fecha del archivo que indica el usuario.
/// </summary>
public static partial class LectorArchivoTerminal
{
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    public static IReadOnlyList<RenglonTerminal> Leer(byte[] contenido, DateOnly fechaArchivo, TerminalTarjetaOptions opciones)
    {
        var texto = Decodificar(contenido);
        var filas = texto.Contains("<tr", StringComparison.OrdinalIgnoreCase) ? FilasHtml(texto) : FilasCsv(texto);
        var c = opciones.Columnas;
        string[] obligatorias = [c.NoOperacion, c.Fecha, c.Importe, c.Estatus, c.Autorizacion];

        var encabezado = filas.FindIndex(f => obligatorias.All(o => f.Contains(o, StringComparer.OrdinalIgnoreCase)));
        if (encabezado < 0)
            throw new BusinessRuleException("TERMINAL_ENCABEZADOS",
                $"El archivo no trae las columnas esperadas: {string.Join(", ", obligatorias)}.");
        var indice = filas[encabezado]
            .Select((nombre, i) => (nombre, i))
            .GroupBy(x => x.nombre, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().i, StringComparer.OrdinalIgnoreCase);

        string? Valor(List<string> fila, string columna) =>
            indice.TryGetValue(columna, out var i) && i < fila.Count && !string.IsNullOrWhiteSpace(fila[i]) ? fila[i].Trim() : null;

        var renglones = new List<RenglonTerminal>();
        for (var r = encabezado + 1; r < filas.Count; r++)
        {
            var fila = filas[r];
            // Las tablas de totales o un encabezado repetido no son ventas.
            var operacion = Valor(fila, c.NoOperacion);
            if (operacion is null || string.Equals(operacion, c.NoOperacion, StringComparison.OrdinalIgnoreCase)) continue;
            var importe = Monto(Valor(fila, c.Importe))
                ?? throw new BusinessRuleException("TERMINAL_IMPORTE", $"La operación {operacion} no trae un importe válido.");
            var estatus = Valor(fila, c.Estatus);
            renglones.Add(new RenglonTerminal(
                operacion,
                Fecha(Valor(fila, c.Fecha), fechaArchivo, operacion),
                importe,
                Valor(fila, c.Moneda),
                Valor(fila, c.Ultimos4),
                Valor(fila, c.Autorizacion),
                estatus,
                string.Equals(estatus, opciones.EstatusAprobada, StringComparison.OrdinalIgnoreCase),
                Monto(Valor(fila, c.Comision)),
                Monto(Valor(fila, c.IvaComision)),
                Monto(Valor(fila, c.Neto)),
                Valor(fila, c.FolioLiquidacion)));
        }
        return renglones;
    }

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

    /// <summary>«dd/MM HH:mm» (año de la fecha del archivo) o «dd/MM/yyyy HH:mm».</summary>
    private static DateTime Fecha(string? valor, DateOnly fechaArchivo, string operacion)
    {
        if (valor is not null)
        {
            if (DateTime.TryParseExact(valor, ["dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy"], Mx,
                    DateTimeStyles.None, out var completa))
                return completa;
            if (DateTime.TryParseExact($"{valor.Split(' ')[0]}/{fechaArchivo.Year} {(valor.Contains(' ') ? valor[(valor.IndexOf(' ') + 1)..] : "00:00")}",
                    "dd/MM/yyyy HH:mm", Mx, DateTimeStyles.None, out var sinAnio))
                return sinAnio;
        }
        throw new BusinessRuleException("TERMINAL_FECHA", $"La operación {operacion} no trae una fecha válida (día/mes hora).");
    }

    [GeneratedRegex(@"<tr[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Fila();

    [GeneratedRegex(@"<t[hd][^>]*>(.*?)</t[hd]>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Celda();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Etiqueta();
}
