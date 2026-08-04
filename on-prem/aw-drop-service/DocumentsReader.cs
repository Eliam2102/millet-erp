using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Millet.AwDropService;

// ============================================================================
// DocumentsReader
//
// Enumera los PDF que A+W exporta en las carpetas configuradas
// (AwDocumentsOptions.Folders). Cada archivo sigue la convención de nombre:
//
//     oferta_<nro>.pdf   → doc_type=oferta, aw_doc_id=<nro>
//     pedido_<nro>.pdf   → doc_type=pedido, aw_doc_id=<nro>
//
// donde <nro> es el número de documento de A+W (= aw_doc_id que el ERP
// correlaciona parseando el log de A+W).
//
// Lo consume el endpoint GET /documents (lista, cursor por ModifiedAt) y
// GET /documents/{filename} (descarga bytes). Reader SOLO LECTURA: jamás
// borra ni mueve nada — los PDF son de A+W.
// ============================================================================

public static class DocumentsReader
{
    /// <summary>
    /// Subcarpeta (dentro de cada carpeta vigilada) donde se mueven los PDF
    /// ya tratados por el ERP. La enumeración de <see cref="ListSince"/> es
    /// top-level (no recursiva), así que <c>archive\</c> NUNCA se re-lista.
    /// </summary>
    public const string ArchiveSubdir = "archive";

    // ^(oferta|pedido)_<digitos>.pdf$ — case-insensitive para tolerar
    // variaciones de A+W (Oferta_, PEDIDO_). El número es estrictamente
    // numérico (aw_doc_id es un entero).
    private static readonly Regex DocumentNameRegex = new(
        @"^(?<type>oferta|pedido)_(?<num>\d+)\.pdf$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(50));

    /// <summary>
    /// Lista los documentos de todas las carpetas con <c>ModifiedAt &gt;
    /// since</c> (estricto). Ordena por <c>ModifiedAt</c> ASC y aplica
    /// <paramref name="limit"/>. Cursor estable: el caller pagina pasando
    /// <c>next_since</c> = ModifiedAt del último item devuelto.
    /// </summary>
    public static IReadOnlyList<DocumentItem> ListSince(
        IReadOnlyList<string> folders,
        DateTimeOffset? since,
        int limit,
        ILogger logger)
    {
        var allItems = new List<DocumentItem>();
        foreach (var folder in folders)
        {
            allItems.AddRange(ListSinceForFolder(folder, since, logger));
        }
        return allItems
            .OrderBy(d => d.ModifiedAt)
            .ThenBy(d => d.Filename, StringComparer.Ordinal) // tiebreak determinístico
            .Take(limit)
            .ToList();
    }

    private static IEnumerable<DocumentItem> ListSinceForFolder(
        string folder,
        DateTimeOffset? since,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(folder)) yield break;

        DirectoryInfo dir;
        try
        {
            dir = new DirectoryInfo(folder);
            if (!dir.Exists) yield break;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Documents: carpeta inaccesible. folder={Folder}", folder);
            yield break;
        }

        // DirectoryInfo.EnumerateFiles trae LastWriteTimeUtc/Length poblados
        // desde la propia enumeración Win32 (FindFirstFile/FindNextFile), sin
        // un stat por archivo — clave para que el costo no escale con el
        // tamaño de la carpeta. Top-level (sin AllDirectories): excluye
        // archive\ automáticamente.
        foreach (var info in dir.EnumerateFiles("*.pdf"))
        {
            var name = info.Name;
            var match = DocumentNameRegex.Match(name);
            if (!match.Success) continue;

            if (!long.TryParse(match.Groups["num"].Value, out var awDocId)) continue;

            DateTimeOffset modifiedAt;
            long sizeBytes;
            try
            {
                modifiedAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
                sizeBytes = info.Length;
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Documents: no se pudo leer metadata. file={File}", info.FullName);
                continue;
            }

            if (since is not null && modifiedAt <= since.Value) continue;

            var docType = match.Groups["type"].Value.ToLowerInvariant();

            yield return new DocumentItem(
                Filename: name,
                DocType: docType,
                AwDocId: awDocId,
                SizeBytes: sizeBytes,
                ModifiedAt: modifiedAt,
                Folder: folder);
        }
    }

    /// <summary>
    /// Mueve un PDF ya tratado por el ERP a la subcarpeta
    /// <see cref="ArchiveSubdir"/> de la carpeta que lo contiene, renombrado
    /// con un sufijo <c>_&lt;utcTimestamp&gt;</c> antes de la extensión
    /// (ej. <c>oferta_4614_20260701T183000123Z.pdf</c>). Idempotente: si el
    /// archivo ya no está en ninguna carpeta caliente (ack repetido), devuelve
    /// <see cref="ArchiveOutcome.NotFound"/> sin error.
    /// </summary>
    public static ArchiveOutcome Archivar(
        IReadOnlyList<string> folders,
        string filename,
        DateTimeOffset utcNow,
        ILogger logger,
        out string archivedPath)
    {
        archivedPath = string.Empty;
        if (!TryResolve(folders, filename, out var source))
        {
            // No está en ninguna carpeta caliente: ya archivado o nunca existió.
            return ArchiveOutcome.NotFound;
        }

        var hotFolder = Path.GetDirectoryName(source)!;
        var archiveDir = Path.Combine(hotFolder, ArchiveSubdir);
        var stem = Path.GetFileNameWithoutExtension(filename);
        var ext = Path.GetExtension(filename);
        var stamp = utcNow.ToString("yyyyMMddTHHmmssfff", System.Globalization.CultureInfo.InvariantCulture) + "Z";
        var target = Path.Combine(archiveDir, $"{stem}_{stamp}{ext}");

        try
        {
            Directory.CreateDirectory(archiveDir);
            if (File.Exists(target)) File.Delete(target); // colisión improbable (mismo ms)
            File.Move(source, target);
            archivedPath = target;
            logger.LogInformation("Documents archived. filename={Filename} target={Target}", filename, target);
            return ArchiveOutcome.Archived;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Documents: no se pudo archivar. file={File}", source);
            return ArchiveOutcome.Error;
        }
    }

    /// <summary>
    /// Resuelve un <paramref name="filename"/> (sin ruta) a su path completo
    /// dentro de alguna de las carpetas configuradas, validando el nombre
    /// con la convención y aplicando un path-traversal check contra cada
    /// carpeta. Devuelve <c>false</c> si el nombre es inválido o el archivo
    /// no existe en ninguna carpeta.
    /// </summary>
    public static bool TryResolve(
        IReadOnlyList<string> folders,
        string filename,
        out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(filename) || !DocumentNameRegex.IsMatch(filename))
        {
            return false;
        }

        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;

            string folderFull;
            string candidate;
            try
            {
                folderFull = Path.GetFullPath(folder);
                candidate = Path.GetFullPath(Path.Combine(folderFull, filename));
            }
            catch
            {
                continue;
            }

            var folderWithSep = folderFull.EndsWith(Path.DirectorySeparatorChar)
                ? folderFull
                : folderFull + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(folderWithSep, StringComparison.OrdinalIgnoreCase))
            {
                continue; // resuelve fuera de la carpeta — defensa traversal
            }

            if (File.Exists(candidate))
            {
                fullPath = candidate;
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Una entrada del endpoint <c>GET /documents</c>. Identifica un PDF
/// exportado por A+W con su tipo, número de documento y metadata de
/// archivo para que el ERP lo correlacione y descargue.
/// </summary>
public sealed record DocumentItem(
    string Filename,
    string DocType,
    long AwDocId,
    long SizeBytes,
    DateTimeOffset ModifiedAt,
    string Folder);

/// <summary>Resultado de <see cref="DocumentsReader.Archivar"/>.</summary>
public enum ArchiveOutcome
{
    /// <summary>Movido a <c>archive\</c>.</summary>
    Archived,
    /// <summary>No estaba en ninguna carpeta caliente (ack repetido / nombre inválido). Idempotente.</summary>
    NotFound,
    /// <summary>Falla de I/O al mover (reintentar el próximo ciclo).</summary>
    Error,
}
