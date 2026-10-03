using System.Text;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Importacion;

/// <summary>
/// Cuerpo de vista previa / perfilado / aplicar. El archivo llega como CSV en base64 (el servidor
/// decodifica: BOM, UTF-8 con respaldo Windows-1252, delimitador) o como tabla de texto crudo
/// (<c>.xlsx</c> leído en el cliente). El servidor es el único normalizador (§20.2).
/// </summary>
public sealed record ImportacionRequest(
    string? Fuente,
    string? ArchivoNombre,
    string? CsvBase64,
    IReadOnlyList<string>? Columnas,
    IReadOnlyList<IReadOnlyList<string?>>? Filas,
    string? Huella);

/// <summary>Tabla cruda ya decodificada. <c>Fila</c> = número de registro del archivo (cabecera = 1).</summary>
public sealed record TablaCruda(
    IReadOnlyList<string> Columnas,
    IReadOnlyList<(int Fila, string?[] Celdas)> Filas,
    int Vacias,
    bool CodificacionFallback,
    int CaracteresReemplazo);

public static class LectorTabla
{
    private static readonly char[] Delimitadores = [',', ';', '\t'];

    public static TablaCruda Leer(ImportacionRequest r)
    {
        var fallback = false;
        List<string?[]> registros;
        if (r.Columnas is { Count: > 0 })
            registros = [[.. r.Columnas], .. (r.Filas ?? []).Select(f => f.ToArray())];
        else if (!string.IsNullOrWhiteSpace(r.CsvBase64))
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(r.CsvBase64); }
            catch (FormatException)
            {
                throw new BusinessRuleException("CONTAB_IMPORT_ARCHIVO_INVALIDO", "El contenido no es base64 válido.");
            }
            registros = Csv(Decodificar(bytes, out fallback));
        }
        else
            throw new BusinessRuleException("CONTAB_IMPORT_ARCHIVO_VACIO", "El archivo no contiene filas.");

        if (registros.Count == 0 || registros[0].All(c => FormatoCatalogo.Texto(c) is null))
            throw new BusinessRuleException("CONTAB_IMPORT_ARCHIVO_VACIO", "El archivo no tiene fila de cabeceras.");

        var cabeceras = registros[0].Select(c => FormatoCatalogo.Texto(c) ?? string.Empty).ToList();
        var filas = new List<(int, string?[])>();
        var vacias = 0;
        var reemplazos = 0;
        for (var i = 1; i < registros.Count; i++)
        {
            var celdas = registros[i];
            if (celdas.All(c => FormatoCatalogo.Texto(c) is null)) { vacias++; continue; }
            reemplazos += celdas.Sum(c => c?.Count(ch => ch == '�') ?? 0);
            filas.Add((i + 1, celdas));
        }
        if (filas.Count == 0)
            throw new BusinessRuleException("CONTAB_IMPORT_ARCHIVO_VACIO", "El archivo no contiene filas de datos.");
        return new TablaCruda(cabeceras, filas, vacias, fallback, reemplazos);
    }

    /// <summary>BOM UTF-8/UTF-16; UTF-8 estricto con respaldo Windows-1252 (⇒ advertencia).</summary>
    internal static string Decodificar(byte[] b, out bool fallback)
    {
        fallback = false;
        if (b is [0xEF, 0xBB, 0xBF, ..]) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
        if (b is [0xFF, 0xFE, ..]) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
        if (b is [0xFE, 0xFF, ..]) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
        try { return new UTF8Encoding(false, true).GetString(b); }
        catch (DecoderFallbackException)
        {
            fallback = true;
            return Encoding.GetEncoding(1252).GetString(b);
        }
    }

    static LectorTabla() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>CSV RFC 4180 mínimo: comillas, comillas dobles escapadas, saltos dentro de comillas, CRLF/LF; delimitador autodetectado.</summary>
    internal static List<string?[]> Csv(string texto)
    {
        var primera = texto.Split('\n', 2)[0];
        var delim = Delimitadores.MaxBy(d => primera.Count(c => c == d));
        var registros = new List<string?[]>();
        var fila = new List<string?>();
        var sb = new StringBuilder();
        var comillas = false;
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (comillas)
            {
                if (c != '"') sb.Append(c);
                else if (i + 1 < texto.Length && texto[i + 1] == '"') { sb.Append('"'); i++; }
                else comillas = false;
            }
            else if (c == '"') comillas = true;
            else if (c == delim) { fila.Add(sb.ToString()); sb.Clear(); }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n') i++;
                fila.Add(sb.ToString()); sb.Clear();
                registros.Add([.. fila]); fila.Clear();
            }
            else sb.Append(c);
        }
        if (sb.Length > 0 || fila.Count > 0) { fila.Add(sb.ToString()); registros.Add([.. fila]); }
        return registros;
    }
}
