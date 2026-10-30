using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Tesoreria.Domain.Conciliaciones.ExtractoParser;

/// <summary>
/// Parser de estado de cuenta bancario con perfil BBVA (T9.2).
/// Soporta formato CSV / separado por comas, puntos y comas o tabuladores.
/// Calcula una firma criptográfica única (SHA-256) por línea para garantizar
/// idempotencia y evitar duplicados al reimportar (CA9.5).
/// </summary>
public sealed class BbvaExtractoParser : IExtractoParser
{
    private static readonly string[] SeparadoresLinea = ["\r\n", "\r", "\n"];
    private static readonly string[] FormatosFecha = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "yyyy/MM/dd", "ddMMyyyy"];

    public string PerfilBanco => "BBVA";

    public ResultadoParseExtracto Parse(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return Parse(reader.ReadToEnd());
    }

    public ResultadoParseExtracto Parse(string contenido)
    {
        if (string.IsNullOrWhiteSpace(contenido))
            throw new BusinessRuleException("EXTRACTO_VACIO", "El archivo de extracto bancario está vacío.");

        var lineasRaw = contenido
            .Split(SeparadoresLinea, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lineasRaw.Count == 0)
            throw new BusinessRuleException("EXTRACTO_VACIO", "El archivo de extracto bancario no contiene renglones.");

        char delimitador = DetectarDelimitador(lineasRaw);
        int indiceEncabezado = -1;
        int colFecha = -1, colDesc = -1, colRef = -1, colCargo = -1, colAbono = -1, colSaldo = -1;

        for (int i = 0; i < Math.Min(15, lineasRaw.Count); i++)
        {
            var partes = SplitCsv(lineasRaw[i], delimitador);
            for (int c = 0; c < partes.Length; c++)
            {
                var h = Normalizar(partes[c]);
                if (h.Contains("FECHA")) colFecha = c;
                else if (h.Contains("DESCRIP") || h.Contains("CONCEPTO") || h.Contains("DETALLE") || h.Contains("MOTIVO")) colDesc = c;
                else if (h.Contains("REFER") || h.Contains("RASTREO") || h.Contains("AUTORIZ") || h.Contains("FOLIO") || h.Contains("CHEQUE")) colRef = c;
                else if (h.Contains("CARGO") || h.Contains("RETIRO") || h.Contains("DEBITO") || h.Contains("EGRESO")) colCargo = c;
                else if (h.Contains("ABONO") || h.Contains("DEPOSIT") || h.Contains("CREDITO") || h.Contains("INGRESO")) colAbono = c;
                else if (h.Contains("SALDO")) colSaldo = c;
            }

            if (colFecha >= 0 && (colDesc >= 0 || colRef >= 0) && (colCargo >= 0 || colAbono >= 0))
            {
                indiceEncabezado = i;
                break;
            }
        }

        // Si no se encontró fila de encabezado explícita, usar mapeo por defecto estándar BBVA
        // [0: Fecha, 1: Descripcion, 2: Referencia, 3: Cargo, 4: Abono, 5: Saldo]
        if (indiceEncabezado == -1)
        {
            colFecha = 0;
            colDesc = 1;
            colRef = 2;
            colCargo = 3;
            colAbono = 4;
            colSaldo = 5;
            indiceEncabezado = -1;
        }

        var lineasParseadas = new List<LineaExtractoParseada>();
        int numLinea = 1;
        decimal? saldoInicial = null;
        decimal? saldoFinal = null;

        for (int i = indiceEncabezado + 1; i < lineasRaw.Count; i++)
        {
            var fila = lineasRaw[i];
            var partes = SplitCsv(fila, delimitador);
            if (partes.Length <= Math.Max(colFecha, Math.Max(colCargo, colAbono)))
                continue;

            if (!TryParseDate(partes.ElementAtOrDefault(colFecha), out var fecha))
                continue;

            string descripcion = colDesc >= 0 && colDesc < partes.Length ? partes[colDesc].Trim() : string.Empty;
            string? referencia = colRef >= 0 && colRef < partes.Length ? partes[colRef].Trim() : null;
            if (string.IsNullOrWhiteSpace(referencia)) referencia = null;

            decimal? cargo = colCargo >= 0 && colCargo < partes.Length ? ParseDecimal(partes[colCargo]) : null;
            decimal? abono = colAbono >= 0 && colAbono < partes.Length ? ParseDecimal(partes[colAbono]) : null;
            decimal? saldo = colSaldo >= 0 && colSaldo < partes.Length ? ParseDecimal(partes[colSaldo]) : null;

            if (cargo.HasValue && cargo.Value <= 0) cargo = null;
            if (abono.HasValue && abono.Value <= 0) abono = null;

            if (cargo is null && abono is null)
                continue;

            if (saldo.HasValue)
            {
                saldoInicial ??= saldo;
                saldoFinal = saldo;
            }

            string hash = ComputarLineaHash(fecha, descripcion, referencia, cargo, abono);

            lineasParseadas.Add(new LineaExtractoParseada(
                numLinea++,
                fecha,
                descripcion,
                referencia,
                cargo,
                abono,
                saldo,
                hash));
        }

        if (lineasParseadas.Count == 0)
            throw new BusinessRuleException("EXTRACTO_SIN_MOVIMIENTOS", "No se encontraron movimientos válidos en el archivo.");

        return new ResultadoParseExtracto(
            lineasParseadas,
            saldoInicial,
            saldoFinal,
            lineasParseadas.Count);
    }

    private static char DetectarDelimitador(List<string> lineas)
    {
        string muestra = string.Join("\n", lineas.Take(5));
        int comas = muestra.Count(c => c == ',');
        int puntosYComas = muestra.Count(c => c == ';');
        int tabs = muestra.Count(c => c == '\t');

        if (tabs > comas && tabs > puntosYComas) return '\t';
        if (puntosYComas > comas) return ';';
        return ',';
    }

    private static string[] SplitCsv(string fila, char delimitador)
    {
        var partes = new List<string>();
        bool dentroComillas = false;
        var actual = new StringBuilder();

        for (int i = 0; i < fila.Length; i++)
        {
            char c = fila[i];
            if (c == '"')
            {
                dentroComillas = !dentroComillas;
            }
            else if (c == delimitador && !dentroComillas)
            {
                partes.Add(actual.ToString().Trim(' ', '"'));
                actual.Clear();
            }
            else
            {
                actual.Append(c);
            }
        }
        partes.Add(actual.ToString().Trim(' ', '"'));
        return partes.ToArray();
    }

    private static bool TryParseDate(string? texto, out DateOnly fecha)
    {
        fecha = default;
        if (string.IsNullOrWhiteSpace(texto)) return false;

        string limpio = texto.Trim();
        return DateOnly.TryParseExact(limpio, FormatosFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha) ||
               DateOnly.TryParse(limpio, CultureInfo.GetCultureInfo("es-MX"), DateTimeStyles.None, out fecha);
    }

    private static decimal? ParseDecimal(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;

        string limpio = texto.Replace("$", "")
                             .Replace(" ", "")
                             .Replace(",", "")
                             .Trim();

        // Paréntesis para negativos: (100.50) -> -100.50
        if (limpio.StartsWith('(') && limpio.EndsWith(')'))
        {
            limpio = string.Concat("-", limpio.AsSpan(1, limpio.Length - 2));
        }

        if (decimal.TryParse(limpio, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val))
            return Math.Abs(val); // Cargo y abono se manejan positivos según su columna

        return null;
    }

    private static string Normalizar(string texto)
    {
        return texto.Trim()
            .ToUpperInvariant()
            .Replace("Á", "A")
            .Replace("É", "E")
            .Replace("Í", "I")
            .Replace("Ó", "O")
            .Replace("Ú", "U");
    }

    public static string ComputarLineaHash(DateOnly fecha, string descripcion, string? referencia, decimal? cargo, decimal? abono)
    {
        string raw = $"{fecha:O}|{descripcion.Trim().ToUpperInvariant()}|{referencia?.Trim().ToUpperInvariant() ?? ""}|{cargo?.ToString("F2", CultureInfo.InvariantCulture) ?? ""}|{abono?.ToString("F2", CultureInfo.InvariantCulture) ?? ""}";
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
