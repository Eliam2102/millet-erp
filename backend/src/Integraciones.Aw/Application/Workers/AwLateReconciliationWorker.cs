using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.IntegrationEvents;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Application.Workers;

/// <summary>
/// <see cref="IHostedService"/> que rescata cotizaciones en
/// <c>FailedDrop</c> con kind <c>aw_processing_timeout</c> cuando A+W
/// procesa el EDI <b>después</b> del timeout sync del drop service.
///
/// <para>
/// <b>Flow del ciclo (PR-4 del feature late-reconciliation):</b>
/// <list type="number">
///   <item>Query cotizaciones candidatas: <c>Estado=FailedDrop</c> + kind
///         timeout + el último <c>Envio</c> dentro de la ventana
///         (<c>MaxWindowHours</c>).</item>
///   <item>Llama <see cref="IAwCompletionsReader.ListSinceAsync"/> con
///         <c>since</c> = el envio más antiguo de la lista.</item>
///   <item>Por cada candidata, busca su <c>Filename</c> en las
///         completions.</item>
///   <item>Si outcome=success, transita a <c>Correlated</c> via
///         <see cref="EntidadExterna.MarcarCorrelacionadaDirectamente"/>
///         (con flag <c>permitirDesdeFailedDrop=true</c>) y publica
///         <see cref="AwPedidoCorrelacionado"/>.</item>
///   <item>Si outcome=failed, transita a <c>FailedCorrelation</c> via
///         <see cref="EntidadExterna.MarcarCorrelacionFallidaDirectamente"/>
///         y publica <see cref="AwCotizacionRechazadaPorAw"/>.</item>
///   <item>Si outcome=stuck, no transita — el log archivado tampoco vio
///         a A+W procesar; espera al siguiente ciclo.</item>
///   <item>Cuenta cotizaciones <b>fuera</b> de ventana como
///         <c>late_reconciliation_misses</c> (señal de alerta).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Sin Service Bus:</b> a diferencia del <see cref="AwDropWorker"/>,
/// este worker es timer-based (Task.Delay con intervalo configurable). No
/// requiere SB para registrarse — vive en cualquier ambiente con DbContext.
/// </para>
///
/// <para>
/// <b>Singleton + IHostedService:</b> patrón estándar del módulo para que
/// el health check reciba la misma instancia que el host ejecuta y vea
/// <see cref="IsRunning"/> correctamente.
/// </para>
/// </summary>
public sealed class AwLateReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IntegracionesAwOptions _options;
    private readonly DocumentSyncKick _kick;
    private readonly ILogger<AwLateReconciliationWorker> _logger;

    private volatile bool _running;

    /// <summary>True mientras el loop esté activo (health check).</summary>
    public bool IsRunning => _running;

    public AwLateReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<IntegracionesAwOptions> options,
        DocumentSyncKick kick,
        ILogger<AwLateReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _kick = kick;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.LateReconciliation.IntervalSeconds);
        _running = true;
        _logger.LogInformation(
            "AwLateReconciliationWorker iniciado. interval={Interval}s window={WindowH}h batch={Batch}",
            interval.TotalSeconds,
            _options.LateReconciliation.MaxWindowHours,
            _options.LateReconciliation.BatchSize);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ReconcileOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "AwLateReconciliationWorker: ciclo falló — se reanuda en el siguiente intervalo.");
                    IntegracionesAwMeter.LateReconciliationErrors.Add(1);
                }

                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            _running = false;
            _logger.LogInformation("AwLateReconciliationWorker detenido.");
        }
    }

    /// <summary>
    /// Un ciclo de reconciliación. <c>internal</c> para que los tests lo
    /// invoquen directo sin levantar el host.
    /// </summary>
    internal async Task ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();
        using var _ = empresaContext.Bypass();

        var db = sp.GetRequiredService<IntegracionesAwDbContext>();
        var reader = sp.GetRequiredService<IAwCompletionsReader>();
        var dropAdapter = sp.GetRequiredService<IAwDropAdapter>();
        var publisher = sp.GetRequiredService<IIntegrationEventPublisher>();
        var notifier = sp.GetRequiredService<IIntegracionesAwNotifier>();
        var agentRealtime = sp.GetRequiredService<IAgentRealtimePublisher>();
        var clock = sp.GetRequiredService<IClock>();

        var now = clock.UtcNow;
        var windowStart = now.AddHours(-_options.LateReconciliation.MaxWindowHours);

        // 1. Candidatas: FailedDrop con kind=aw_processing_timeout cuyo último
        // envío cayó DENTRO de la ventana y tiene Filename.
        //
        // Dos queries planas + filtrado/join en memoria. EF Core con InMemory
        // y Postgres traducen igual ambos casos (Where + OrderBy simples) sin
        // dependencia de tipos custom proyectados en Select — record types y
        // anonymous types con sub-queries son frágiles cross-provider.
        var failed = await db.EntidadesExternas
            .Where(e => e.Estado == EstadoEntidad.FailedDrop
                     && e.LastError != null
                     && e.LastError.StartsWith("[aw_processing_timeout]"))
            .ToListAsync(cancellationToken);

        if (failed.Count == 0)
        {
            await RecordMissesAsync(db, windowStart, cancellationToken);
            return;
        }

        var ids = failed.Select(e => e.Id).ToList();
        var envios = await db.Envios
            .Where(en => ids.Contains(en.EntidadExternaId))
            .ToListAsync(cancellationToken);

        var ultimoEnvioPorEntidad = envios
            .GroupBy(en => en.EntidadExternaId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(en => en.AttemptNumber).First());

        var candidates = failed
            .Select(e => new CandidatePair(
                e,
                ultimoEnvioPorEntidad.GetValueOrDefault(e.Id)))
            .Where(x => x.UltimoEnvio is { Filename: not null }
                     && x.UltimoEnvio.StartedAt > windowStart)
            .ToList();

        if (candidates.Count == 0)
        {
            await RecordMissesAsync(db, windowStart, cancellationToken);
            return;
        }

        // 2. Cursor since = envío más antiguo de las candidatas.
        var since = candidates.Min(c => c.UltimoEnvio!.StartedAt);
        var page = await reader.ListSinceAsync(
            since, _options.LateReconciliation.BatchSize, cancellationToken);

        // 3. Index por filename (mantiene el match más reciente si hay duplicados).
        var byFilename = page.Completions
            .GroupBy(c => c.Filename, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(c => c.ParsedAt).First(),
                StringComparer.OrdinalIgnoreCase);

        int correlated = 0, failedCount = 0;
        var toArchive = new List<string>();
        foreach (var x in candidates)
        {
            var filename = x.UltimoEnvio!.Filename!;
            if (!byFilename.TryGetValue(filename, out var match)) continue;

            switch (match.Outcome)
            {
                case DropOutcome.Success:
                    await ApplySuccessAsync(
                        x.Entidad, match, now, publisher, notifier, agentRealtime, cancellationToken);
                    correlated++;
                    if (match.AwDocId is { } docId)
                    {
                        toArchive.Add(
                            $"{System.IO.Path.GetFileNameWithoutExtension(match.Filename)}." +
                            $"{docId.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                    }
                    break;

                case DropOutcome.Failed:
                    await ApplyFailedAsync(
                        x.Entidad, match, now, publisher, notifier, agentRealtime, cancellationToken);
                    failedCount++;
                    break;

                case DropOutcome.Stuck:
                case DropOutcome.Unknown:
                default:
                    // El log archivado no resolvió outcome — esperamos al
                    // siguiente ciclo (A+W puede procesar y el drop service
                    // archivar un log success/failed más adelante).
                    break;
            }
        }

        if (correlated > 0 || failedCount > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        // Ack: mueve a archive\ los marcadores ya grabados. Best-effort (ver
        // puerto): si el ack falla, el marcador queda en Results\ y el próximo
        // ciclo lo re-lista y re-archiva. Solo después del SaveChanges.
        foreach (var markerName in toArchive)
        {
            await dropAdapter.ArchiveResultAsync(markerName, cancellationToken);
        }

        // Fetch de PDF por evento: si correlacionamos algo, despierta al
        // AwDocumentSyncWorker para que busque los PDF ni bien A+W los exporte.
        if (correlated > 0)
        {
            _kick.Kick();
        }

        await RecordMissesAsync(db, windowStart, cancellationToken);

        _logger.LogInformation(
            "Late reconciliation ciclo. candidatas={Cands} correlated={Corr} failed={Fail} since={Since}",
            candidates.Count, correlated, failedCount, since);
    }

    private static async Task ApplySuccessAsync(
        EntidadExterna entidad,
        AwCompletionItem match,
        DateTimeOffset nowUtc,
        IIntegrationEventPublisher publisher,
        IIntegracionesAwNotifier notifier,
        IAgentRealtimePublisher agentRealtime,
        CancellationToken cancellationToken)
    {
        entidad.MarcarCorrelacionadaDirectamente(
            awDocId: match.AwDocId,
            correlatedAt: match.ParsedAt,
            diagnosticLog: $"late-reconcile lane={match.Lane}",
            permitirDesdeFailedDrop: true);

        await publisher.PublishAsync(
            new AwPedidoCorrelacionado(
                EmpresaId: entidad.EmpresaId,
                OcurridoEn: nowUtc,
                AggregateId: entidad.Id,
                QuoteReference: entidad.ReferenciaExterna,
                AwDocId: match.AwDocId ?? 0L,
                CorrelatedAt: match.ParsedAt),
            cancellationToken);

        await notifier.NotifyCorrelacionExitosaAsync(
            empresaId: entidad.EmpresaId,
            aggregateId: entidad.Id,
            quoteReference: entidad.ReferenciaExterna,
            awDocId: match.AwDocId ?? 0L,
            correlatedAt: match.ParsedAt,
            cancellationToken);

        await agentRealtime.PublishCotizacionActualizadaAsync(entidad, cancellationToken);

        IntegracionesAwMeter.LateCorrelated.Add(1);
    }

    private static async Task ApplyFailedAsync(
        EntidadExterna entidad,
        AwCompletionItem match,
        DateTimeOffset nowUtc,
        IIntegrationEventPublisher publisher,
        IIntegracionesAwNotifier notifier,
        IAgentRealtimePublisher agentRealtime,
        CancellationToken cancellationToken)
    {
        // El aggregate exige >=1 código + mensaje no vacío. Si el log no
        // emitió códigos identificables, sintetizamos uno genérico
        // (mismo patrón que AwDropWorker.HandleDropOutcomeAsync).
        var codes = match.ErrorCodes.Count > 0
            ? match.ErrorCodes
            : (IReadOnlyList<string>)["unknown_aw_rejection"];
        var msg = match.ErrorMessage ?? "A+W rechazó el EDI (sin códigos identificados).";

        entidad.MarcarCorrelacionFallidaDirectamente(
            errorCodes: codes,
            errorMessage: msg,
            failedAt: match.ParsedAt,
            diagnosticLog: $"late-reconcile lane={match.Lane}",
            permitirDesdeFailedDrop: true);

        await publisher.PublishAsync(
            new AwCotizacionRechazadaPorAw(
                EmpresaId: entidad.EmpresaId,
                OcurridoEn: nowUtc,
                AggregateId: entidad.Id,
                QuoteReference: entidad.ReferenciaExterna,
                AwDocId: match.AwDocId,
                ErrorCodes: codes,
                ErrorMessage: msg,
                FailedAt: match.ParsedAt),
            cancellationToken);

        await notifier.NotifyCotizacionEstadoActualizadoAsync(
            empresaId: entidad.EmpresaId,
            aggregateId: entidad.Id,
            quoteReference: entidad.ReferenciaExterna,
            estado: EstadoEntidad.FailedCorrelation.ToString(),
            ocurridoEn: nowUtc,
            cancellationToken: cancellationToken);

        await agentRealtime.PublishCotizacionActualizadaAsync(entidad, cancellationToken);

        IntegracionesAwMeter.LateFailed.Add(1);
    }

    /// <summary>
    /// Cuenta cotizaciones en <c>FailedDrop</c> con kind timeout cuyo
    /// último envío cayó <b>fuera</b> de la ventana — el worker no las
    /// puede rescatar automáticamente. Las exporta a la métrica
    /// <c>late_reconciliation_misses</c> para alerta.
    /// </summary>
    private static async Task RecordMissesAsync(
        IntegracionesAwDbContext db,
        DateTimeOffset windowStart,
        CancellationToken cancellationToken)
    {
        var fuera = await db.EntidadesExternas
            .Where(e => e.Estado == EstadoEntidad.FailedDrop
                     && e.LastError != null
                     && e.LastError.StartsWith("[aw_processing_timeout]")
                     && !db.Envios.Any(en => en.EntidadExternaId == e.Id
                                          && en.StartedAt > windowStart))
            .CountAsync(cancellationToken);

        if (fuera > 0)
        {
            IntegracionesAwMeter.LateReconciliationMisses.Add(fuera);
        }
    }

    /// <summary>
    /// Tuple-like record para mantener tracking del <see cref="EntidadExterna"/>
    /// junto al envío proyectado en la misma query.
    /// </summary>
    private sealed record CandidatePair(EntidadExterna Entidad, Envio? UltimoEnvio);
}
