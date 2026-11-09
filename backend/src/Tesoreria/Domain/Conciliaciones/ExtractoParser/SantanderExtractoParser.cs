using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Tesoreria.Domain.Conciliaciones.ExtractoParser;

/// <summary>
/// Parser de estado de cuenta bancario con perfil Santander / Getnet (cuenta 8138).
/// Soporta formato CSV/delimitado con encabezados, fechas compactas (DDMMAAAA)
/// y columna combinada de Cargo/Abono con signo explícito (+ depósitos, - comisiones).
/// Calcula una firma criptográfica única (SHA-256) por línea para garantizar idempotencia (CA9.5).
/// </summary>
public sealed class SantanderExtractoParser : IExtractoParser
{
    private static readonly string[] SeparadoresLinea = ["\r\n", "\r", "\n"];
    private static readonly string[] FormatosFecha = ["ddMMyyyy", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "yyyy/MM/dd"];

    public string PerfilBanco => "SANTANDER";

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
        int colFecha = -1, colDesc = -1, colRef = -1, colCargoAbono = -1, colCargo = -1, colAbono = -1, colSaldo = -1;

        for (int i = 0; i < Math.Min(15, lineasRaw.Count); i++)
        {
            var partes = SplitCsv(lineasRaw[i], delimitador);
            for (int c = 0; c < partes.Length; c++)
            {
                var h = Normalizar(partes[c]);
                if (h.Contains("FECHA")) colFecha = c;
                else if (h.Contains("DESCRIP") || h.Contains("CONCEPTO") || h.Contains("DETALLE") || h.Contains("MOTIVO") || h.Contains("MOVIMIENTO")) colDesc = c;
                else if (h.Contains("REFER") || h.Contains("RASTREO") || h.Contains("AUTORIZ") || h.Contains("FOLIO") || h.Contains("CHEQUE") || h.Contains("OPERACION")) colRef = c;
                else if (h.Contains("CARGO/ABONO") || h.Contains("CARGO / ABONO") || h.Contains("CARGO_ABONO")) colCargoAbono = c;
                else if (h.Contains("CARGO") || h.Contains("RETIRO") || h.Contains("DEBITO") || h.Contains("EGRESO")) colCargo = c;
                else if (h.Contains("ABONO") || h.Contains("DEPOSIT") || h.Contains("CREDITO") || h.Contains("INGRESO")) colAbono = c;
                else if (h.Contains("IMPORTE") || h.Contains("MONTO") || h.Contains("VALOR"))
                {
                    if (colCargoAbono == -1 && colCargo == -1 && colAbono == -1) colCargoAbono = c;
                }
                else if (h.Contains("SALDO")) colSaldo = c;
            }

            bool tieneFecha = colFecha >= 0;
            bool tieneImporte = colCargoAbono >= 0 || (colCargo >= 0 || colAbono >= 0);

            if (tieneFecha && tieneImporte)
            {
                indiceEncabezado = i;
                break;
            }
        }

        // Si no se encontró fila de encabezado explícita, usar mapeo por defecto
        if (indiceEncabezado == -1)
        {
            colFecha = 0;
            colDesc = 1;
            colRef = 2;
            colCargoAbono = 3;
            colSaldo = 4;
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
            if (partes.Length <= colFecha)
                continue;

            if (!TryParseDate(partes.ElementAtOrDefault(colFecha), out var fecha))
                continue;

            string descripcion = colDesc >= 0 && colDesc < partes.Length ? partes[colDesc].Trim() : string.Empty;
            string? referencia = colRef >= 0 && colRef < partes.Length ? partes[colRef].Trim() : null;
            if (string.IsNullOrWhiteSpace(referencia)) referencia = null;

            decimal? cargo = null;
            decimal? abono = null;

            if (colCargoAbono >= 0 && colCargoAbono < partes.Length)
            {
                ParseSignedAmount(partes[colCargoAbono], out cargo, out abono);
            }
            else
            {
                cargo = colCargo >= 0 && colCargo < partes.Length ? ParseDecimal(partes[colCargo]) : null;
                abono = colAbono >= 0 && colAbono < partes.Length ? ParseDecimal(partes[colAbono]) : null;
            }

            if (cargo.HasValue && cargo.Value <= 0) cargo = null;
            if (abono.HasValue && abono.Value <= 0) abono = null;

            if (cargo is null && abono is null)
                continue;

            decimal? saldo = colSaldo >= 0 && colSaldo < partes.Length ? ParseDecimal(partes[colSaldo]) : null;

            if (saldo.HasValue)
            {
                saldoInicial ??= saldo;
                saldoFinal = saldo;
            }

            string hash = BbvaExtractoParser.ComputarLineaHash(fecha, descripcion, referencia, cargo, abono);

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

    private static void ParseSignedAmount(string? texto, out decimal? cargo, out decimal? abono)
    {
        cargo = null;
        abono = null;
        if (string.IsNullOrWhiteSpace(texto)) return;

        string limpio = texto.Replace("$", "")
                             .Replace(" ", "")
                             .Replace(",", "")
                             .Trim();

        bool esNegativo = false;
        if (limpio.StartsWith('-'))
        {
            esNegativo = true;
            limpio = limpio[1..].Trim();
        }
        else if (limpio.StartsWith('+'))
        {
            esNegativo = false;
            limpio = limpio[1..].Trim();
        }
        else if (limpio.StartsWith('(') && limpio.EndsWith(')'))
        {
            esNegativo = true;
            limpio = limpio[1..^1].Trim();
        }

        limpio = limpio.TrimStart('0');
        if (string.IsNullOrEmpty(limpio) || limpio.StartsWith('.'))
        {
            limpio = "0" + limpio;
        }

        if (decimal.TryParse(limpio, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val))
        {
            if (val <= 0) return;

            if (esNegativo)
            {
                cargo = val;
            }
            else
            {
                abono = val;
            }
        }
    }

    private static decimal? ParseDecimal(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;

        string limpio = texto.Replace("$", "")
                             .Replace(" ", "")
                             .Replace(",", "")
                             .Trim();

        if (limpio.StartsWith('(') && limpio.EndsWith(')'))
        {
            limpio = string.Concat("-", limpio.AsSpan(1, limpio.Length - 2));
        }

        bool negativo = limpio.StartsWith('-');
        if (negativo || limpio.StartsWith('+'))
        {
            limpio = limpio[1..].Trim();
        }

        limpio = limpio.TrimStart('0');
        if (string.IsNullOrEmpty(limpio) || limpio.StartsWith('.'))
        {
            limpio = "0" + limpio;
        }

        if (negativo)
        {
            limpio = "-" + limpio;
        }

        if (decimal.TryParse(limpio, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val))
            return Math.Abs(val);

        return null;
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
}
