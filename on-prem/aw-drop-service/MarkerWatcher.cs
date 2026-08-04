namespace Millet.AwDropService;

// ============================================================================
// MarkerWatcher
//
// Espera (poll) a que el customizing de A+W escriba el marcador
// cot_<REF>.<AWDOCID> en Results\ para el EDI que el drop service acaba de
// escribir. Reemplaza al antiguo AwLogWatcher que parseaba un last_batch.log
// compartido: con marcador por-EDI la correlación es inequívoca (el nombre
// del archivo ES el pedido) y no hay contenido que parsear.
//
//   * Marcador encontrado dentro del timeout → Found + aw_doc_id del nombre.
//   * Timeout sin marcador → NotFound (el drop responde outcome=stuck).
//     Cubre "aún no procesado" y "A+W rechazó" — indistinguibles (Idea 4).
// ============================================================================

public sealed class MarkerWatcher
{
    private readonly TimeSpan _pollInterval;

    public MarkerWatcher(int pollIntervalMs)
    {
        if (pollIntervalMs < 100)
            throw new ArgumentOutOfRangeException(nameof(pollIntervalMs),
                "PollIntervalMs debe ser >= 100ms.");
        _pollInterval = TimeSpan.FromMilliseconds(pollIntervalMs);
    }

    /// <summary>
    /// Espera hasta que aparezca el marcador de <paramref name="ediFilename"/>
    /// en <paramref name="resultsFolder"/> o hasta agotar
    /// <paramref name="timeout"/>. El marcador se busca por el basename del
    /// EDI (<c>cot_&lt;REF&gt;</c>).
    /// </summary>
    public async Task<MarkerWaitResult> WaitForMarkerAsync(
        string resultsFolder,
        string ediFilename,
        DateTime droppedAt,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(resultsFolder);
        var ediBasename = Path.GetFileNameWithoutExtension(ediFilename);

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ResultsReader.TryFindMarker(resultsFolder, ediBasename, out var markerName, out var awDocId))
            {
                var waitedMs = (int)(DateTime.UtcNow - droppedAt).TotalMilliseconds;
                return new MarkerWaitResult(
                    Found: true,
                    MarkerName: markerName,
                    AwDocId: awDocId,
                    WaitedMs: waitedMs);
            }

            await Task.Delay(_pollInterval, cancellationToken);
        }

        return new MarkerWaitResult(
            Found: false,
            MarkerName: null,
            AwDocId: null,
            WaitedMs: (int)(DateTime.UtcNow - droppedAt).TotalMilliseconds);
    }
}

/// <summary>
/// Resultado de <see cref="MarkerWatcher.WaitForMarkerAsync"/>. Si
/// <see cref="Found"/> es <c>true</c>, <see cref="AwDocId"/> y
/// <see cref="MarkerName"/> están poblados.
/// </summary>
public sealed record MarkerWaitResult(
    bool Found,
    string? MarkerName,
    long? AwDocId,
    int WaitedMs);
