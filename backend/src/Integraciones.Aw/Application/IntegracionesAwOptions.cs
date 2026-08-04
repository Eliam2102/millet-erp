namespace Millet.Integraciones.Aw.Application;

/// <summary>
/// Settings del módulo Integraciones.Aw. Bindeadas desde la sección
/// <c>IntegracionesAw</c> de <c>appsettings.json</c>. Schema FLAT por
/// decisión de PR C — los handlers leen propiedades directo, así que
/// mantenemos el objeto plano en lugar de re-anidar por subsistema.
///
/// <para>
/// // PLATFORM-TODO(&lt;IntegracionesAwSettings&gt;): tabla
/// integraciones_aw.settings (por empresa) para que el ajuste no
/// requiera redeploy. PR posterior. Hoy las settings son globales del
/// proceso (mismo valor para todas las empresas).
/// </para>
///
/// <para>
/// <b>PR #201 — limpieza:</b> retiradas
/// <c>CorrelationTimeoutMinutes</c>, <c>PollingIntervalSeconds</c>,
/// <c>MaxConsecutiveFailuresBeforePauseExpiry</c>, <c>CorrelationBatchSize</c>
/// (settings del antiguo <c>AwCorrelationWorker</c> que fue retirado al
/// migrar a flow callback per-EDI). Si vivían en appsettings, ahora son
/// ignoradas (no rompen — el binder solo bindea lo que reconoce).
/// </para>
/// </summary>
public sealed class IntegracionesAwOptions
{
    public const string SectionName = "IntegracionesAw";

    /// <summary>
    /// Máximo de reintentos del drop al on-prem antes de marcar
    /// terminal. Default 5.
    /// </summary>
    public int DropMaxAttempts { get; set; } = 5;

    /// <summary>
    /// Base address del drop service on-prem (HTTP). El tráfico se rutea
    /// vía Hybrid Connection (HC transparente al código). Default
    /// <c>http://SER-DATA:5000</c>.
    /// </summary>
    public string DropServiceBaseAddress { get; set; } = "http://SER-DATA:5000";

    /// <summary>
    /// API key que el drop service exige en el header <c>X-API-Key</c>.
    /// En QA/Prod es KV ref <c>aw-drop-service-api-key</c>; en dev local
    /// vacío (drop service local sin auth en fase 1).
    /// </summary>
    public string DropServiceApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Timeout HTTP del drop por intento. Default 180s — el drop service
    /// per-EDI (PR #199) bloquea hasta tener outcome de A+W
    /// (waitTimeoutSeconds=120 + margen para round-trip HC + parseo +
    /// move de log). El timeout HTTP del cliente debe ser estrictamente
    /// mayor para que el drop service tenga chance de responder
    /// outcome=stuck en lugar de que el cliente cancele primero.
    /// </summary>
    public int DropTimeoutSeconds { get; set; } = 180;

    /// <summary>
    /// MaxConcurrentCalls del <c>ServiceBusProcessor</c> en
    /// <c>AwDropWorker</c>. Default 1 (procesamiento serial — fase 1).
    /// Mantener en 1 mientras el drop service tenga 1 lane activa; de lo
    /// contrario varios drops compiten por la misma lane y solo el primero
    /// pasa, resto bloquea hasta su turno (waste de hora SB).
    /// </summary>
    public int DropMaxConcurrentMessages { get; set; } = 1;

    /// <summary>
    /// Timeout de queries SQL contra A+W on-prem (segundos). Default 5s.
    /// Usado por <c>IAwSqlReader</c> / <c>HybridConnectionAwSqlReader</c>
    /// — el flow per-EDI no consulta SQL para correlación (el log de A+W
    /// trae el <c>aw_doc_id</c>), pero el reader queda disponible para
    /// integraciones futuras (CFDI, Almacén, CxC) según D1 del CLAUDE.md.
    /// </summary>
    public int SqlQueryTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Timeout duro para <c>SqlConnection.OpenAsync</c> (segundos).
    /// Default 15s. Protege contra hangs cuando SQL Server on-prem no
    /// acepta TCP — el <c>OpenAsync</c> del SDK puede quedarse colgado
    /// indefinidamente aunque el connection string declare
    /// <c>Connection Timeout=10</c>. Ver PR #197.
    /// </summary>
    public int SqlConnectTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Catálogo de sucursales válidas: <c>{ código corto → nombre display }</c>.
    /// Ej. <c>{ "CIR": "CIRCUITO", "CHI": "CHICHI SUAREZ", "CAN": "CANCUN",
    /// "CON": "CONKAL" }</c>. El validador del comando
    /// <c>RegistrarCotizacionEdiCommand</c> rechaza con
    /// <c>AW_SUCURSAL_INVALIDA</c> cualquier valor fuera de las keys.
    ///
    /// <para>
    /// El código (key) se persiste en <c>entidad_externa.sucursal</c>. Ya NO
    /// se embebe en el filename del EDI (el depto viaja dentro del EDI y lo
    /// sobrescribe A+W con un único customizing; ver runbook aw-customizing
    /// v4). Se conserva para el push realtime al Glass Agent y trazabilidad.
    /// </para>
    ///
    /// <para>
    /// Si en el futuro las sucursales necesitan editarse sin redeploy
    /// (UF admin) o llevar más metadata (código A+W asociado, etc.),
    /// migrar a tabla catálogo <c>integraciones_aw.sucursal</c>. Hoy
    /// vivir en config es suficiente para 4 valores estáticos.
    /// </para>
    /// </summary>
    public Dictionary<string, string> Sucursales { get; set; } = new();

    /// <summary>
    /// Sub-options del <c>AwLateReconciliationWorker</c> (PR-4 del feature
    /// late reconciliation). El worker corre periódicamente, consulta el
    /// endpoint <c>GET /completions</c> del drop service y rescata
    /// cotizaciones que A+W procesó después del timeout sync.
    /// </summary>
    public LateReconciliationOptions LateReconciliation { get; set; } = new();

    /// <summary>
    /// Sub-options del <c>AwDocumentSyncWorker</c>. El worker corre
    /// periódicamente, consulta el endpoint <c>GET /documents</c> del drop
    /// service, descarga los PDF que A+W exportó (oferta/pedido) y los sube
    /// a Blob Storage para exponerlos al Glass Agent.
    /// </summary>
    public DocumentSyncOptions DocumentSync { get; set; } = new();
}

/// <summary>
/// Configuración del <c>AwDocumentSyncWorker</c>.
/// </summary>
public sealed class DocumentSyncOptions
{
    /// <summary>
    /// Cadencia del ciclo IDLE (sin PDF pendientes recientes). Default 120s.
    /// Es la red de seguridad; el camino rápido lo maneja
    /// <see cref="FastIntervalSeconds"/> + el <c>DocumentSyncKick</c>.
    /// </summary>
    public int IntervalSeconds { get; set; } = 120;

    /// <summary>
    /// Cadencia RÁPIDA mientras haya cotizaciones correlacionadas sin PDF
    /// dentro de la ventana caliente (<see cref="FastWindowMinutes"/>). El
    /// worker polea a este ritmo tras un kick de correlación hasta que el PDF
    /// aparece (A+W lo exporta segundos después de crear el pedido) o la
    /// entidad sale de la ventana. Default 5s.
    /// </summary>
    public int FastIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Ventana (minutos desde la correlación) durante la cual una cotización
    /// sin PDF mantiene al worker en cadencia rápida. Pasada la ventana, cae al
    /// barrido idle (que igual la levanta por <see cref="LookbackHours"/>).
    /// Acota el poll rápido para no quedar atascado si A+W nunca exporta el PDF.
    /// Default 5 min.
    /// </summary>
    public int FastWindowMinutes { get; set; } = 5;

    /// <summary>
    /// Ventana hacia atrás (horas) que el worker escanea en
    /// <c>/documents?since=</c> cada ciclo. Suficientemente amplia para
    /// cubrir PDF de cotizaciones correlacionadas recientemente que aún no
    /// tienen archivo. Default 72h.
    /// </summary>
    public int LookbackHours { get; set; } = 72;

    /// <summary>Tope de items por página del endpoint <c>/documents</c>. Default 500.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// Tope de páginas a drenar por ciclo (defensa contra carpetas muy
    /// pobladas). Default 20 → hasta 10k documentos por ciclo.
    /// </summary>
    public int MaxPagesPerCycle { get; set; } = 20;
}

/// <summary>
/// Configuración del <c>AwLateReconciliationWorker</c>.
/// </summary>
public sealed class LateReconciliationOptions
{
    /// <summary>
    /// Cadencia del ciclo de reconciliación. Default 120s — alineado con
    /// el timeout sync del drop service (<c>WaitTimeoutSeconds</c>). Si
    /// A+W procesa tarde dentro de un ciclo, en el siguiente la cotización
    /// queda rescatada.
    /// </summary>
    public int IntervalSeconds { get; set; } = 120;

    /// <summary>
    /// Ventana máxima hacia atrás. Cotizaciones que cayeron en
    /// <c>FailedDrop</c> hace más de este tiempo se ignoran — quedan
    /// disponibles para correlación manual (G3 del feature, futuro PR-5).
    /// Default 24 horas.
    /// </summary>
    public int MaxWindowHours { get; set; } = 24;

    /// <summary>
    /// Tope de items por página del endpoint <c>/completions</c>. Default
    /// 2000 (el endpoint clampea a su propio max).
    /// </summary>
    public int BatchSize { get; set; } = 2000;
}
