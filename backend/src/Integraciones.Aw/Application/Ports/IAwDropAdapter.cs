namespace Millet.Integraciones.Aw.Application.Ports;

/// <summary>
/// Puerto out-going que abstrae el HTTP drop del archivo EDI al
/// servicio on-prem (drop service .NET 8 que recibe via Hybrid
/// Connection). Lo consume <c>AwDropWorker</c>.
///
/// <para>
/// <b>Evolución del contrato (PR #198):</b> el drop service ahora bloquea
/// el HTTP hasta que A+W terminó de procesar el EDI (modelo "per-EDI"),
/// y reporta el outcome final en la misma respuesta. Los campos nuevos
/// (<see cref="DropResult.Outcome"/>, <see cref="DropResult.AwDocId"/>,
/// <see cref="DropResult.AwErrorCodes"/>, etc.) son opcionales con
/// defaults — implementaciones viejas que solo confirman el drop sin
/// esperar A+W siguen siendo válidas (devuelven
/// <see cref="DropOutcome.Unknown"/>). El cableado del nuevo flow al
/// worker llega en PR #201.
/// </para>
/// </summary>
public interface IAwDropAdapter
{
    Task<DropResult> SendEdiAsync(string filename, string ediContent, CancellationToken cancellationToken);

    /// <summary>
    /// Ack al drop service: mueve el marcador <c>cot_&lt;REF&gt;.&lt;AWDOCID&gt;</c>
    /// de <c>Results\</c> a <c>Results\archive\</c> (POST
    /// <c>/results/{name}/archive</c>). El ERP lo llama SOLO después de grabar
    /// durablemente el aw_doc_id — el move marca "procesado".
    ///
    /// <para>
    /// <b>Best-effort:</b> nunca lanza. Si el ack falla (red, 5xx), el marcador
    /// queda en <c>Results\</c> y <c>GET /completions</c> lo re-listará; el
    /// AwLateReconciliationWorker lo vuelve a archivar (idempotente). El dato
    /// ya está durable en el ERP, así que un ack perdido no pierde nada.
    /// </para>
    /// </summary>
    Task ArchiveResultAsync(string markerName, CancellationToken cancellationToken);
}

/// <summary>
/// Resultado del drop al servicio on-prem. Los campos extendidos
/// (Outcome, AwDocId, AwErrorCodes, AwErrorMessage, AwDiagnosticLog,
/// Lane, WaitedMs) los puebla el drop service "per-EDI" después de
/// observar el outcome de A+W (archivo en Save\ o Fail\ + parseo del
/// log <c>g_lsUdv[0]</c>).
/// </summary>
public sealed record DropResult(
    int BytesWritten,
    string Path,
    DateTimeOffset WrittenAt,
    DropOutcome Outcome = DropOutcome.Unknown,
    long? AwDocId = null,
    IReadOnlyList<string>? AwErrorCodes = null,
    string? AwErrorMessage = null,
    string? AwDiagnosticLog = null,
    string? Lane = null,
    int? WaitedMs = null);

/// <summary>
/// Outcome final del procesamiento del EDI por A+W, reportado por el
/// drop service en el mismo HTTP del drop.
/// </summary>
public enum DropOutcome : short
{
    /// <summary>
    /// El drop service no reportó outcome (implementación vieja o flow
    /// "fire-and-forget"). Backward compat. El worker debe transicionar
    /// a <c>AwaitingCorrelation</c> y dejar que el correlation worker
    /// resuelva — comportamiento pre-PR-#198.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// A+W procesó el EDI correctamente. <see cref="DropResult.AwDocId"/>
    /// es el ID del doc creado en A+W (de <c>(4614)</c> en el log).
    /// </summary>
    Success = 1,

    /// <summary>
    /// A+W rechazó el EDI (códigos terminales como <c>(1555)</c>). El
    /// archivo terminó en la carpeta <c>Fail\</c>. Los códigos y el
    /// mensaje vienen del parseo del log.
    /// </summary>
    Failed = 2,

    /// <summary>
    /// El drop service escribió el archivo en <c>Work\</c> pero A+W no
    /// lo procesó dentro del timeout esperado (scheduler de A+W pausado,
    /// HC degradado, etc.). Estado operativo — reintentable.
    /// </summary>
    Stuck = 3,
}
