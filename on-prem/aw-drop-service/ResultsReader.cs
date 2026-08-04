using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Millet.AwDropService;

// ============================================================================
// ResultsReader
//
// Lee los MARCADORES que el customizing de A+W escribe en la carpeta Results\
// DESPUÉS de crear el pedido. Cada marcador es un archivo VACÍO cuyo nombre
// carga toda la info (Idea 4 — el nombre es suficiente, sin contenido):
//
//     cot_<REF>.<AWDOCID>        →  ref="cot_<REF>", aw_doc_id=<AWDOCID>
//     ej. cot_Q-2026-00451.10432218
//
// Como el <REF> no lleva puntos (es Q-AAAA-NNNNN), el ÚLTIMO segmento (todo
// dígitos) es el aw_doc_id (auftragsnummer del pedido). El marcador SOLO
// existe en éxito; si A+W rechaza el EDI no escribe nada (→ el drop cae en
// timeout=stuck).
//
// Tres consumidores:
//   * MarkerWatcher (flujo síncrono) → TryFindMarker por el basename del EDI.
//   * GET /completions (reconciliación tardía) → ListSince por cursor mtime.
//   * POST /results/{name}/archive → Archivar (mueve a archive\ tras el ack
//     del ERP). El estado "procesado" es la ubicación: Results\ = pendiente,
//     Results\archive\ = procesado.
// ============================================================================

public static class ResultsReader
{
    /// <summary>
    /// Subcarpeta (dentro de Results\) donde se mueven los marcadores ya
    /// procesados por el ERP. La enumeración de <see cref="ListSince"/> es
    /// top-level (no recursiva), así que <c>archive\</c> NUNCA se re-lista.
    /// </summary>
    public const string ArchiveSubdir = "archive";

    // cot_<REF>.<AWDOCID> — el ref capta todo antes del último punto; el
    // aw_doc_id es el último segmento, estrictamente numérico. Greedy `.+`
    // + `\.` + `\d+$` parte en el último punto (el REF no tiene puntos).
    private static readonly Regex MarkerNameRegex = new(
        @"^(?<ref>.+)\.(?<docid>\d+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    /// <summary>
    /// Parsea el nombre de un marcador. <paramref name="reference"/> es el
    /// basename del EDI (<c>cot_&lt;REF&gt;</c>); <paramref name="awDocId"/>
    /// el <c>auftragsnummer</c>. <c>false</c> si el nombre no matchea.
    /// </summary>
    public static bool TryParseMarker(string name, out string reference, out long awDocId)
    {
        reference = string.Empty;
        awDocId = 0;
        var m = MarkerNameRegex.Match(name);
        if (!m.Success) return false;
        if (!long.TryParse(m.Groups["docid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out awDocId))
            return false;
        reference = m.Groups["ref"].Value;
        return true;
    }

    /// <summary>
    /// Busca el marcador correspondiente a <paramref name="ediBasename"/>
    /// (nombre del EDI sin extensión, ej. <c>cot_Q-2026-00451</c>) en
    /// <paramref name="resultsFolder"/>. Glob dirigido <c>{ediBasename}.*</c>
    /// — barato aunque haya muchos marcadores. Devuelve el nombre y el
    /// aw_doc_id del primer match cuyo ref coincide exactamente.
    /// </summary>
    public static bool TryFindMarker(
        string resultsFolder, string ediBasename, out string markerName, out long awDocId)
    {
        markerName = string.Empty;
        awDocId = 0;
        DirectoryInfo dir;
        try
        {
            dir = new DirectoryInfo(resultsFolder);
            if (!dir.Exists) return false;
        }
        catch (Exception) { return false; }

        foreach (var info in dir.EnumerateFiles(ediBasename + ".*"))
        {
            if (TryParseMarker(info.Name, out var refPart, out var docid)
                && string.Equals(refPart, ediBasename, StringComparison.Ordinal))
            {
                markerName = info.Name;
                awDocId = docid;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Lista los marcadores de <c>Results\</c> (top-level) con
    /// <c>ModifiedAt &gt; since</c> (estricto), ordenados por mtime ASC y
    /// aplicando <paramref name="limit"/>. Cada uno se mapea a un
    /// <see cref="CompletionItem"/> con <c>outcome="success"</c> (el marcador
    /// solo existe en éxito) y el aw_doc_id del nombre. Cursor estable: el
    /// caller pagina pasando <c>next_since</c> = ParsedAt del último item.
    /// </summary>
    public static IReadOnlyList<CompletionItem> ListSince(
        string resultsFolder,
        DateTimeOffset? since,
        int limit,
        ILogger logger)
    {
        var items = new List<CompletionItem>();
        DirectoryInfo dir;
        try
        {
            dir = new DirectoryInfo(resultsFolder);
            if (!dir.Exists) return items;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Results: carpeta inaccesible. folder={Folder}", resultsFolder);
            return items;
        }

        // EnumerateFiles top-level (sin AllDirectories) excluye archive\.
        foreach (var info in dir.EnumerateFiles("*"))
        {
            if (!TryParseMarker(info.Name, out var refPart, out var docid)) continue;

            DateTimeOffset modifiedAt;
            try
            {
                modifiedAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Results: no se pudo leer metadata. file={File}", info.FullName);
                continue;
            }

            if (since is not null && modifiedAt <= since.Value) continue;

            items.Add(new CompletionItem(
                // Reconstruye el nombre del EDI para que el ERP matchee por
                // Filename (== envio.Filename = cot_<REF>.edi).
                Filename: refPart + ".edi",
                Outcome: "success",
                AwDocId: docid,
                AwErrorCodes: Array.Empty<string>(),
                AwErrorMessage: null,
                AwDiagnosticLog: null,
                ParsedAt: modifiedAt,
                Lane: DropServiceOptions.WireLane));
        }

        return items
            .OrderBy(c => c.ParsedAt)
            .ThenBy(c => c.Filename, StringComparer.Ordinal) // tiebreak determinístico
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Mueve un marcador YA PROCESADO por el ERP a <c>Results\archive\</c>,
    /// renombrado con sufijo <c>_&lt;utcTimestamp&gt;</c>. El ERP lo llama
    /// SOLO después de grabar el aw_doc_id (el move = "procesado"). Idempotente:
    /// si ya no está (ack repetido) → <see cref="ArchiveOutcome.NotFound"/>.
    /// </summary>
    public static ArchiveOutcome Archivar(
        string resultsFolder,
        string name,
        DateTimeOffset utcNow,
        ILogger logger,
        out string archivedPath)
    {
        archivedPath = string.Empty;
        if (!TryResolve(resultsFolder, name, out var source))
        {
            return ArchiveOutcome.NotFound;
        }

        var archiveDir = Path.Combine(Path.GetDirectoryName(source)!, ArchiveSubdir);
        var stamp = utcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture) + "Z";
        var target = Path.Combine(archiveDir, $"{name}_{stamp}");

        try
        {
            Directory.CreateDirectory(archiveDir);
            if (File.Exists(target)) File.Delete(target); // colisión improbable (mismo ms)
            File.Move(source, target);
            archivedPath = target;
            logger.LogInformation("Results archived. name={Name} target={Target}", name, target);
            return ArchiveOutcome.Archived;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Results: no se pudo archivar. file={File}", source);
            return ArchiveOutcome.Error;
        }
    }

    /// <summary>
    /// Resuelve <paramref name="name"/> (sin ruta) a su path dentro de
    /// <paramref name="resultsFolder"/>, validando la convención de marcador
    /// y aplicando path-traversal check. <c>false</c> si el nombre es inválido
    /// o no existe (top-level; NO busca en archive\).
    /// </summary>
    public static bool TryResolve(string resultsFolder, string name, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(name) || !MarkerNameRegex.IsMatch(name))
        {
            return false;
        }

        string folderFull;
        string candidate;
        try
        {
            folderFull = Path.GetFullPath(resultsFolder);
            candidate = Path.GetFullPath(Path.Combine(folderFull, name));
        }
        catch
        {
            return false;
        }

        var folderWithSep = folderFull.EndsWith(Path.DirectorySeparatorChar)
            ? folderFull
            : folderFull + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(folderWithSep, StringComparison.OrdinalIgnoreCase))
        {
            return false; // resuelve fuera de Results\ — defensa traversal
        }

        if (File.Exists(candidate))
        {
            fullPath = candidate;
            return true;
        }
        return false;
    }
}

/// <summary>
/// Una entrada del endpoint <c>GET /completions</c>. Bajo el modelo de
/// marcadores (Idea 4) el <c>Outcome</c> siempre es <c>"success"</c> y los
/// campos de error van vacíos/null — el contrato se conserva para no romper
/// la deserialización del ERP (<c>AwCompletionItem</c>).
/// </summary>
public sealed record CompletionItem(
    string Filename,
    string Outcome,
    long? AwDocId,
    IReadOnlyList<string> AwErrorCodes,
    string? AwErrorMessage,
    string? AwDiagnosticLog,
    DateTimeOffset ParsedAt,
    string Lane);
