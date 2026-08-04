using System.Diagnostics.Metrics;

namespace Millet.Integraciones.Aw.Application;

/// <summary>
/// Métricas custom del módulo Integraciones.Aw. Registrar en
/// <c>Program.cs</c> via
/// <c>builder.Services.AddOpenTelemetry().WithMetrics(m =&gt; m.AddMeter(IntegracionesAwMeter.Name))</c>
/// para que las métricas lleguen a Application Insights / OTel Distro.
///
/// <para>
/// Counters/histograms son <c>static readonly</c> — un único <c>Meter</c>
/// por proceso, sin necesidad de DI.
/// </para>
///
/// <para>
/// <b>PR #201 — limpieza:</b> retiradas
/// <c>PollingCycleDurationMs</c>, <c>PollingMatchesFound</c>,
/// <c>CorrelationLatencyMs</c> (basadas en el ciclo polling del antiguo
/// <c>AwCorrelationWorker</c>). El gauge <c>HybridConnectionHealthy</c>
/// también se retiró porque su única fuente era el polling worker; con
/// el flow per-EDI la salud del HC se infiere de <c>DropFailed</c> tag
/// <c>outcome=stuck</c>. Si surge necesidad de un gauge dedicado de
/// HC, agregar un healthcheck custom que use <c>ISqlConnectionFactory</c>
/// y exportarlo via OTel meter.
/// </para>
/// </summary>
public static class IntegracionesAwMeter
{
    public const string Name = "Millet.Integraciones.Aw";

    private static readonly Meter _meter = new(Name);

    public static readonly Counter<long> DropSuccess =
        _meter.CreateCounter<long>("aw.cotizacion.drop_success",
            description: "Drops donde A+W procesó exitosamente el EDI (outcome=success).");

    public static readonly Counter<long> DropFailed =
        _meter.CreateCounter<long>("aw.cotizacion.drop_failed",
            description: "Drops fallidos. Tag outcome=stuck|rejected|http_error; tag is_terminal=true|false.");

    public static readonly Counter<long> Correlated =
        _meter.CreateCounter<long>("aw.cotizacion.correlated",
            description: "Cotizaciones correlacionadas exitosamente con A+W (incluye awDocId).");

    public static readonly Counter<long> CorrelacionRechazada =
        _meter.CreateCounter<long>("aw.cotizacion.correlacion_rechazada",
            description: "Cotizaciones que A+W rechazó al procesar el EDI (códigos 1550/1603/1555).");

    public static readonly Histogram<double> DropWaitDurationMs =
        _meter.CreateHistogram<double>("aw.drop.wait_duration_ms",
            unit: "ms",
            description: "Tiempo que el drop service esperó a A+W (waited_ms del response). " +
                         "p50/p99 = SLO de cadencia del scheduler A+W.");

    // ───────── PR-4 feature late-reconciliation ─────────

    public static readonly Counter<long> LateCorrelated =
        _meter.CreateCounter<long>("aw.cotizacion.late_correlated",
            description: "Cotizaciones rescatadas por el AwLateReconciliationWorker (success): A+W procesó el EDI tarde y el log llegó al endpoint /completions.");

    public static readonly Counter<long> LateFailed =
        _meter.CreateCounter<long>("aw.cotizacion.late_failed",
            description: "Cotizaciones rescatadas por el AwLateReconciliationWorker pero A+W terminó rechazando el EDI (outcome=failed).");

    public static readonly Counter<long> LateReconciliationMisses =
        _meter.CreateCounter<long>("aw.cotizacion.late_reconciliation_misses",
            description: "Cotizaciones en FailedDrop con kind aw_processing_timeout que excedieron MaxWindowHours sin match. Señal de alerta — si sostenido, considerar SQL fallback (Fase 2).");

    public static readonly Counter<long> LateReconciliationErrors =
        _meter.CreateCounter<long>("aw.cotizacion.late_reconciliation_errors",
            description: "Errores en el ciclo del AwLateReconciliationWorker (HTTP /completions, DB, etc.).");

    // ───────── feature aw-documentos-pdf ─────────

    public static readonly Counter<long> DocumentoAdjuntado =
        _meter.CreateCounter<long>("aw.documento.adjuntado",
            description: "PDF de A+W (oferta/pedido) descargados del drop service y subidos a Blob Storage.");

    public static readonly Counter<long> DocumentSyncErrors =
        _meter.CreateCounter<long>("aw.documento.sync_errors",
            description: "Errores en el ciclo del AwDocumentSyncWorker (HTTP /documents, descarga, blob, DB).");
}
