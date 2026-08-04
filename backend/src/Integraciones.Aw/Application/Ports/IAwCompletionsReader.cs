namespace Millet.Integraciones.Aw.Application.Ports;

/// <summary>
/// Puerto out-going que abstrae la lectura del endpoint
/// <c>GET /completions</c> del drop service on-prem. Lo consume el
/// <c>AwLateReconciliationWorker</c> (PR-4) para recuperar el outcome
/// de EDIs que A+W procesó <b>después</b> del timeout sync del drop
/// service.
///
/// <para>
/// La semántica del endpoint es cursor-based (<c>since</c> estricto +
/// <c>limit</c>); el caller pagina pasando <see cref="AwCompletionsPage.NextSince"/>
/// recibido en la respuesta anterior. Implementaciones de prueba pueden
/// devolver páginas sin servidor real.
/// </para>
/// </summary>
public interface IAwCompletionsReader
{
    /// <summary>
    /// Lista completions con <c>ParsedAt &gt; since</c>. Si <paramref name="since"/>
    /// es <c>null</c>, devuelve desde la entrada más antigua disponible
    /// (limitada por la retención de <c>Processed\</c> del drop service).
    /// </summary>
    /// <param name="since">
    /// Cursor estricto. Pasar <see cref="AwCompletionsPage.NextSince"/> de
    /// la página anterior para paginar sin overlap.
    /// </param>
    /// <param name="limit">
    /// Tope de items por página. El servidor clampea a su rango interno
    /// (<c>[1, 2000]</c>).
    /// </param>
    /// <exception cref="AwCompletionsException">
    /// Falla HTTP (auth, red, 5xx, body inválido). <c>IsTransient=true</c>
    /// para errores recuperables, <c>false</c> para configuración rota.
    /// </exception>
    Task<AwCompletionsPage> ListSinceAsync(
        DateTimeOffset? since,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>
/// Página de completions devuelta por <see cref="IAwCompletionsReader"/>.
/// </summary>
public sealed record AwCompletionsPage(
    IReadOnlyList<AwCompletionItem> Completions,
    DateTimeOffset? NextSince);

/// <summary>
/// Una entrada del endpoint <c>GET /completions</c>: identifica un drop
/// archivado en <c>Processed\</c> con su outcome (decidido por el drop
/// service en su flow sync) y los datos parseados del log de A+W.
/// </summary>
/// <param name="Filename">Nombre del EDI original (ej. <c>cot_NIN_Q-2026-00346.edi</c>).</param>
/// <param name="Outcome">Outcome decidido por el drop service. Reusa el enum del puerto <see cref="IAwDropAdapter"/>.</param>
/// <param name="AwDocId"><c>(4615) [Documento]=N</c> parseado del log; null si outcome != Success.</param>
/// <param name="ErrorCodes">Códigos de A+W (ej. <c>1555</c>); vacío si Success.</param>
/// <param name="ErrorMessage">Mensaje user-friendly construido a partir de los códigos.</param>
/// <param name="DiagnosticLog">Slice del log A+W (truncado por el drop service).</param>
/// <param name="ParsedAt">Timestamp embebido en el filename de <c>Processed\</c>.</param>
/// <param name="Lane">Nombre de la lane del drop service.</param>
public sealed record AwCompletionItem(
    string Filename,
    DropOutcome Outcome,
    long? AwDocId,
    IReadOnlyList<string> ErrorCodes,
    string? ErrorMessage,
    string? DiagnosticLog,
    DateTimeOffset ParsedAt,
    string Lane);
