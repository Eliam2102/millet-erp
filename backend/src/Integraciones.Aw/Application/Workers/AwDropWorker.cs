using System.Text.Json;
using Azure.Messaging.ServiceBus;
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
/// Worker que consume <c>integraciones-aw-events</c> /
/// <c>drop-subscription</c> (filter SqlFilter sobre
/// <see cref="AwCotizacionRecibida"/>) y dispara el drop al on-prem
/// via <see cref="IAwDropAdapter"/>.
///
/// <para>
/// <b>Flujo del handler de mensaje (PR #201 — callback per-EDI):</b>
/// <list type="number">
///   <item>Deserializa <c>AwCotizacionRecibida</c> del body.</item>
///   <item>En scope nuevo, bajo bypass: carga <c>EntidadExterna</c>.</item>
///   <item>Idempotencia: si Estado ∉ {Submitted, FailedDrop}, complete + skip.</item>
///   <item>Llama <see cref="IAwDropAdapter.SendEdiAsync"/>. El drop service
///         bloquea hasta tener outcome de A+W (max ~120s).</item>
///   <item>Switch sobre <see cref="DropResult.Outcome"/>:
///     <list type="bullet">
///       <item><c>Success</c> → <c>MarcarCorrelacionadaDirectamente</c> +
///             publish <see cref="AwPedidoCorrelacionado"/> + notify SignalR
///             + complete.</item>
///       <item><c>Failed</c> → <c>MarcarCorrelacionFallidaDirectamente</c> +
///             publish <see cref="AwCotizacionRechazadaPorAw"/> + notify +
///             complete.</item>
///       <item><c>Stuck</c> → <c>MarcarStuck</c> + publish
///             <see cref="AwEdiEntregaFallida"/> con kind aw_processing_timeout
///             + notify + complete (la cotización queda reintentable desde UI).</item>
///       <item><c>Unknown</c> → fallback legacy (transición a FailedDrop con
///             kind unknown_outcome). Solo ocurre si el drop service responde
///             con versión vieja del contrato — defensa en profundidad.</item>
///     </list>
///   </item>
///   <item>HTTP transient fail (<see cref="AwDropException.IsTransient"/>=true):
///         incrementa retry + abandon → SB redelivery.</item>
///   <item>HTTP terminal fail: MarcarDropFalladoTerminal + DLQ.</item>
///   <item>Excepción inesperada: abandon (preferimos retry).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Sin MediatR commands:</b> el handler hace el work directo
/// (Repository + DbContext.SaveChanges) en vez de via <c>IMediator</c>.
/// Razón: los commands viejos (<c>MarcarEdiEntregado</c>,
/// <c>MarcarPedidoCorrelacionado</c>) se retiraron en PR #201; crear
/// commands nuevos solo para envolver una transición directa del aggregate
/// + un publish es overhead sin valor (no hay validación FluentValidation,
/// no hay reuso entre múltiples callers — sólo este worker).
/// </para>
///
/// <para>
/// <b>Health check:</b> <see cref="IsRunning"/> se setea a true DESPUÉS de
/// que <c>ServiceBusProcessor.StartProcessingAsync</c> tuvo éxito. Si el
/// Start falla (SB down), el flag queda false y el healthcheck reporta
/// Unhealthy en lugar de mentir "Healthy" durante todo el lifetime.
/// </para>
/// </summary>
public sealed class AwDropWorker : BackgroundService
{
    /// <summary>Topic del módulo (TAREA 0 verificó vs Bicep).</summary>
    public const string TopicName = "integraciones-aw-events";

    /// <summary>Subscription real.</summary>
    public const string DropSubscriptionName = "drop-subscription";

    private readonly ServiceBusClient _serviceBusClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IntegracionesAwOptions _options;
    private readonly DocumentSyncKick _kick;
    private readonly ILogger<AwDropWorker> _logger;

    private volatile bool _running;
    private ServiceBusProcessor? _processor;

    /// <summary>True si el processor está running tras un Start exitoso.</summary>
    public bool IsRunning => _running;

    public AwDropWorker(
        ServiceBusClient serviceBusClient,
        IServiceScopeFactory scopeFactory,
        IOptions<IntegracionesAwOptions> options,
        DocumentSyncKick kick,
        ILogger<AwDropWorker> logger)
    {
        _serviceBusClient = serviceBusClient;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _kick = kick;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var processorOptions = new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = _options.DropMaxConcurrentMessages,
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(10),
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
        };

        _processor = _serviceBusClient.CreateProcessor(
            topicName: TopicName,
            subscriptionName: DropSubscriptionName,
            options: processorOptions);

        _processor.ProcessMessageAsync += OnMessageReceivedAsync;
        _processor.ProcessErrorAsync += OnProcessorErrorAsync;

        try
        {
            await _processor.StartProcessingAsync(stoppingToken);
            _running = true;

            _logger.LogInformation(
                "AwDropWorker iniciado. Topic={Topic} Subscription={Sub} MaxConcurrent={Max}",
                TopicName, DropSubscriptionName, _options.DropMaxConcurrentMessages);

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown normal.
            }
        }
        finally
        {
            _running = false;
            if (_processor is { IsProcessing: true })
            {
                try
                {
                    await _processor.StopProcessingAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error parando ServiceBusProcessor durante shutdown.");
                }
            }
            await _processor.DisposeAsync();
            _processor = null;
            _logger.LogInformation("AwDropWorker detenido.");
        }
    }

    private async Task OnMessageReceivedAsync(ProcessMessageEventArgs args)
    {
        var completion = new ServiceBusMessageCompletion(args);
        await ProcessRawMessageAsync(args.Message.Body.ToString(), args.Message.MessageId,
            completion, args.CancellationToken);
    }

    /// <summary>
    /// Procesa el body raw de un mensaje + un <see cref="IMessageCompletion"/>
    /// que abstrae los callbacks de Service Bus (Complete/Abandon/DeadLetter).
    /// Internal para que tests unit invoquen sin levantar SB real.
    /// </summary>
    internal async Task ProcessRawMessageAsync(
        string rawBody,
        string messageId,
        IMessageCompletion completion,
        CancellationToken cancellationToken)
    {
        using var activity = IntegracionesAwActivitySource.Instance.StartActivity(
            "AwDropWorker.ProcessMessage");
        activity?.SetTag("messaging.message_id", messageId);

        AwCotizacionRecibida? evt;
        try
        {
            evt = JsonSerializer.Deserialize<AwCotizacionRecibida>(rawBody);
            if (evt is null)
            {
                throw new JsonException("Body deserializó a null.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Mensaje con payload corrupto. message_id={MessageId}. Dead-letter.",
                messageId);
            await completion.DeadLetterAsync("InvalidPayload", ex.Message, cancellationToken);
            return;
        }

        activity?.SetTag("aw.aggregate_id", evt.AggregateId);
        activity?.SetTag("aw.quote_reference", evt.QuoteReference);
        activity?.SetTag("aw.empresa_id", evt.EmpresaId);

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        var db = sp.GetRequiredService<IntegracionesAwDbContext>();
        var repo = sp.GetRequiredService<IEntidadExternaRepository>();
        var dropAdapter = sp.GetRequiredService<IAwDropAdapter>();
        var publisher = sp.GetRequiredService<IIntegrationEventPublisher>();
        var notifier = sp.GetRequiredService<IIntegracionesAwNotifier>();
        var agentRealtime = sp.GetRequiredService<IAgentRealtimePublisher>();
        var clock = sp.GetRequiredService<IClock>();

        var entidad = await repo.GetByIdCrossEmpresaAsync(evt.AggregateId, cancellationToken);
        if (entidad is null)
        {
            _logger.LogWarning(
                "EntidadExterna {AggregateId} no encontrada (mensaje stale?). Dead-letter.",
                evt.AggregateId);
            await completion.DeadLetterAsync("AggregateNotFound",
                $"AggregateId={evt.AggregateId}", cancellationToken);
            return;
        }

        if (entidad.Estado is not EstadoEntidad.Submitted
            and not EstadoEntidad.FailedDrop)
        {
            _logger.LogInformation(
                "Drop idempotent skip. aggregate_id={AggregateId} estado={Estado}",
                entidad.Id, entidad.Estado);
            await completion.CompleteAsync(cancellationToken);
            return;
        }

        // Si entra desde FailedDrop (reintento), MarcarCorrelacionada/Fallida
        // requieren estado Submitted. Reintentar() resetea contadores y
        // transiciona FailedDrop → Submitted, dejando el agregado listo
        // para el switch sobre outcome más abajo.
        if (entidad.Estado is EstadoEntidad.FailedDrop)
        {
            entidad.Reintentar();
        }

        if (string.IsNullOrEmpty(entidad.EdiContent))
        {
            _logger.LogError(
                "EntidadExterna {AggregateId} sin EdiContent. Dead-letter.",
                entidad.Id);
            await completion.DeadLetterAsync("MissingEdiContent",
                "EntidadExterna.EdiContent es null/empty.", cancellationToken);
            return;
        }

        // Auditoría por intento: registra este drop en `integraciones_aw.envio`
        // con el filename que se manda al on-prem. El AwLateReconciliationWorker
        // matchea entradas de `Processed\` por este Filename cuando A+W procesa
        // el EDI tarde (después del timeout sync del drop service).
        var attemptNumber = await NextAttemptNumberAsync(db, entidad.Id, cancellationToken);
        var startedAt = clock.UtcNow;
        var envio = Envio.Empezar(
            entidad: entidad,
            attemptNumber: attemptNumber,
            startedAt: startedAt,
            dropServiceUrl: _options.DropServiceBaseAddress,
            filename: evt.FilenameSuggestion);
        db.Envios.Add(envio);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var dropResult = await dropAdapter.SendEdiAsync(
                filename: evt.FilenameSuggestion,
                ediContent: entidad.EdiContent,
                cancellationToken: cancellationToken);
            sw.Stop();

            var nowUtc = clock.UtcNow;
            envio.FinalizarExito(
                finishedAt: nowUtc,
                bytesSent: dropResult.BytesWritten,
                httpStatusCode: 200,
                durationMs: (int)sw.ElapsedMilliseconds);

            await HandleDropOutcomeAsync(
                entidad, dropResult, nowUtc, evt.FilenameSuggestion, dropAdapter,
                db, publisher, notifier, agentRealtime, completion, cancellationToken);
        }
        catch (AwDropException ex) when (ex.IsTransient)
        {
            sw.Stop();
            envio.FinalizarFallo(
                finishedAt: clock.UtcNow,
                resultado: ex.Kind == "timeout" ? EstadoEnvio.Timeout : EstadoEnvio.Failed,
                errorMessage: ex.Message,
                errorKind: ex.Kind,
                httpStatusCode: null,
                durationMs: (int)sw.ElapsedMilliseconds);

            entidad.IncrementarRetry(ex.Message, ex.Kind);
            await db.SaveChangesAsync(cancellationToken);

            await completion.AbandonAsync(cancellationToken);

            IntegracionesAwMeter.DropFailed.Add(1,
                new KeyValuePair<string, object?>("outcome", "http_error"),
                new KeyValuePair<string, object?>("is_terminal", false));

            await notifier.NotifyDropFallidoAsync(
                empresaId: entidad.EmpresaId,
                aggregateId: entidad.Id,
                quoteReference: entidad.ReferenciaExterna,
                errorMessage: ex.Message,
                errorKind: ex.Kind,
                retryCount: entidad.RetryCount,
                cancellationToken: cancellationToken);

            // Agent realtime push (best-effort).
            await agentRealtime.PublishCotizacionActualizadaAsync(entidad, cancellationToken);
        }
        catch (AwDropException ex)
        {
            sw.Stop();
            envio.FinalizarFallo(
                finishedAt: clock.UtcNow,
                resultado: EstadoEnvio.Failed,
                errorMessage: ex.Message,
                errorKind: ex.Kind,
                httpStatusCode: null,
                durationMs: (int)sw.ElapsedMilliseconds);

            entidad.MarcarDropFalladoTerminal(ex.Message, ex.Kind);
            await db.SaveChangesAsync(cancellationToken);

            await completion.DeadLetterAsync(ex.Kind, ex.Message, cancellationToken);

            IntegracionesAwMeter.DropFailed.Add(1,
                new KeyValuePair<string, object?>("outcome", "http_error"),
                new KeyValuePair<string, object?>("is_terminal", true));

            await notifier.NotifyDropDeadLetterAsync(
                empresaId: entidad.EmpresaId,
                aggregateId: entidad.Id,
                quoteReference: entidad.ReferenciaExterna,
                errorMessage: ex.Message,
                errorKind: ex.Kind,
                cancellationToken: cancellationToken);

            // Agent realtime push (best-effort).
            await agentRealtime.PublishCotizacionActualizadaAsync(entidad, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Activity error reporting (PR #196): App Insights muestra la
            // dependency con success=False + detail del error.
            System.Diagnostics.Activity.Current?.SetStatus(
                System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            System.Diagnostics.Activity.Current?.AddException(ex);

            _logger.LogError(ex,
                "Error inesperado procesando mensaje. message_id={MessageId} aggregate_id={AggregateId}. Abandon (SB reintentará).",
                messageId, entidad.Id);
            await completion.AbandonAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Nombre del marcador a archivar: <c>cot_&lt;REF&gt;.&lt;AWDOCID&gt;</c>,
    /// derivado del nombre del EDI (<c>cot_&lt;REF&gt;.edi</c>) + el aw_doc_id.
    /// </summary>
    private static string MarkerName(string ediFilename, long awDocId) =>
        $"{System.IO.Path.GetFileNameWithoutExtension(ediFilename)}.{awDocId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    private async Task HandleDropOutcomeAsync(
        EntidadExterna entidad,
        DropResult dropResult,
        DateTimeOffset nowUtc,
        string filename,
        IAwDropAdapter dropAdapter,
        IntegracionesAwDbContext db,
        IIntegrationEventPublisher publisher,
        IIntegracionesAwNotifier notifier,
        IAgentRealtimePublisher agentRealtime,
        IMessageCompletion completion,
        CancellationToken cancellationToken)
    {
        // Histograma de wait (independiente del outcome para que p50/p99
        // refleje SLO real del scheduler A+W).
        if (dropResult.WaitedMs is { } waited)
        {
            IntegracionesAwMeter.DropWaitDurationMs.Record(waited);
        }

        switch (dropResult.Outcome)
        {
            case DropOutcome.Success:
                entidad.MarcarCorrelacionadaDirectamente(
                    awDocId: dropResult.AwDocId,
                    correlatedAt: nowUtc,
                    diagnosticLog: dropResult.AwDiagnosticLog);

                await publisher.PublishAsync(
                    new AwPedidoCorrelacionado(
                        EmpresaId: entidad.EmpresaId,
                        OcurridoEn: nowUtc,
                        AggregateId: entidad.Id,
                        QuoteReference: entidad.ReferenciaExterna,
                        AwDocId: dropResult.AwDocId ?? 0L,
                        CorrelatedAt: nowUtc),
                    cancellationToken);

                await db.SaveChangesAsync(cancellationToken);
                await completion.CompleteAsync(cancellationToken);

                IntegracionesAwMeter.DropSuccess.Add(1);
                IntegracionesAwMeter.Correlated.Add(1);

                await notifier.NotifyCorrelacionExitosaAsync(
                    empresaId: entidad.EmpresaId,
                    aggregateId: entidad.Id,
                    quoteReference: entidad.ReferenciaExterna,
                    awDocId: dropResult.AwDocId ?? 0L,
                    correlatedAt: nowUtc,
                    cancellationToken);

                // Agent realtime push (best-effort).
                await agentRealtime.PublishCotizacionActualizadaAsync(entidad, cancellationToken);

                if (dropResult.AwDocId is { } awDocIdOk)
                {
                    // Ack: mueve el marcador cot_<REF>.<docid> a archive\ ahora
                    // que el aw_doc_id quedó durable. Best-effort (ver puerto).
                    await dropAdapter.ArchiveResultAsync(
                        MarkerName(filename, awDocIdOk), cancellationToken);
                }
                else
                {
                    // Caso defensivo: A+W reportó success pero sin doc id. Por
                    // project memory los IDs siempre están presentes — si esto
                    // aparece es señal de drift operativo (marcador mal formado).
                    _logger.LogWarning(
                        "Drop success sin aw_doc_id. aggregate_id={AggregateId} quote_ref={QuoteRef} " +
                        "lane={Lane} — revisar el marcador que escribe A+W.",
                        entidad.Id, entidad.ReferenciaExterna, dropResult.Lane);
                }

                // Fetch de PDF por evento: el pedido ya existe → su PDF es
                // inminente. Despierta al AwDocumentSyncWorker para que polee
                // rápido en vez de esperar el intervalo idle.
                _kick.Kick();
                break;

            case DropOutcome.Failed:
                var codes = dropResult.AwErrorCodes ?? Array.Empty<string>();
                var message = dropResult.AwErrorMessage ?? "A+W rechazó el EDI (sin códigos identificados).";

                if (codes.Count == 0)
                {
                    // El aggregate exige >=1 código. Sintetizamos uno
                    // genérico para preservar la invariante.
                    codes = new[] { "unknown_aw_rejection" };
                }

                entidad.MarcarCorrelacionFallidaDirectamente(
                    errorCodes: codes,
                    errorMessage: message,
                    failedAt: nowUtc,
                    diagnosticLog: dropResult.AwDiagnosticLog);

                await publisher.PublishAsync(
                    new AwCotizacionRechazadaPorAw(
                        EmpresaId: entidad.EmpresaId,
                        OcurridoEn: nowUtc,
                        AggregateId: entidad.Id,
                        QuoteReference: entidad.ReferenciaExterna,
                        AwDocId: dropResult.AwDocId,
                        ErrorCodes: codes,
                        ErrorMessage: message,
                        FailedAt: nowUtc),
                    cancellationToken);

                await db.SaveChangesAsync(cancellationToken);
                await completion.CompleteAsync(cancellationToken);

                IntegracionesAwMeter.CorrelacionRechazada.Add(1);

                await notifier.NotifyCotizacionEstadoActualizadoAsync(
                    empresaId: entidad.EmpresaId,
                    aggregateId: entidad.Id,
                    quoteReference: entidad.ReferenciaExterna,
                    estado: EstadoEntidad.FailedCorrelation.ToString(),
                    ocurridoEn: nowUtc,
                    cancellationToken: cancellationToken);

                // Agent realtime push (best-effort).
                await agentRealtime.PublishCotizacionActualizadaAsync(entidad, cancellationToken);
                break;

            case DropOutcome.Stuck:
                entidad.MarcarStuck(
                    waitedMs: dropResult.WaitedMs ?? 0,
                    lane: dropResult.Lane);

                await publisher.PublishAsync(
                    new AwEdiEntregaFallida(
                        EmpresaId: entidad.EmpresaId,
                        OcurridoEn: nowUtc,
                        AggregateId: entidad.Id,
                        QuoteReference: entidad.ReferenciaExterna,
                        Error: $"A+W no procesó en {dropResult.WaitedMs ?? 0}ms (lane={dropResult.Lane}).",
                        ErrorKind: "aw_processing_timeout"),
                    cancellationToken);

                await db.SaveChangesAsync(cancellationToken);
                await completion.CompleteAsync(cancellationToken);

                IntegracionesAwMeter.DropFailed.Add(1,
                    new KeyValuePair<string, object?>("outcome", "stuck"),
                    new KeyValuePair<string, object?>("is_terminal", true));

                await notifier.NotifyDropFallidoAsync(
                    empresaId: entidad.EmpresaId,
                    aggregateId: entidad.Id,
                    quoteReference: entidad.ReferenciaExterna,
                    errorMessage: $"A+W no procesó en {dropResult.WaitedMs ?? 0}ms",
                    errorKind: "aw_processing_timeout",
                    retryCount: entidad.RetryCount,
                    cancellationToken: cancellationToken);

                // Agent realtime push (best-effort).
                await agentRealtime.PublishCotizacionActualizadaAsync(entidad, cancellationToken);
                break;

            case DropOutcome.Unknown:
            default:
                // Fallback defensivo: drop service respondió 200 pero sin
                // outcome legible (versión vieja del drop service o body
                // ininterpretable). Marcamos como FailedDrop terminal para
                // que el operador investigue.
                entidad.MarcarDropFalladoTerminal(
                    error: "Drop service no reportó outcome interpretable.",
                    errorKind: "unknown_outcome");

                await db.SaveChangesAsync(cancellationToken);
                await completion.CompleteAsync(cancellationToken);

                IntegracionesAwMeter.DropFailed.Add(1,
                    new KeyValuePair<string, object?>("outcome", "unknown"),
                    new KeyValuePair<string, object?>("is_terminal", true));

                _logger.LogWarning(
                    "Drop outcome=unknown. aggregate_id={AggregateId} — drop service viejo o response sin outcome.",
                    entidad.Id);

                await notifier.NotifyDropFallidoAsync(
                    empresaId: entidad.EmpresaId,
                    aggregateId: entidad.Id,
                    quoteReference: entidad.ReferenciaExterna,
                    errorMessage: "Drop service no reportó outcome interpretable.",
                    errorKind: "unknown_outcome",
                    retryCount: entidad.RetryCount,
                    cancellationToken: cancellationToken);

                // Agent realtime push (best-effort).
                await agentRealtime.PublishCotizacionActualizadaAsync(entidad, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Adapta los callbacks de un <see cref="ProcessMessageEventArgs"/>
    /// real al puerto <see cref="IMessageCompletion"/>.
    /// </summary>
    private sealed class ServiceBusMessageCompletion : IMessageCompletion
    {
        private readonly ProcessMessageEventArgs _args;
        public ServiceBusMessageCompletion(ProcessMessageEventArgs args) { _args = args; }

        public Task CompleteAsync(CancellationToken cancellationToken) =>
            _args.CompleteMessageAsync(_args.Message, cancellationToken);

        public Task AbandonAsync(CancellationToken cancellationToken) =>
            _args.AbandonMessageAsync(_args.Message, propertiesToModify: null, cancellationToken);

        public Task DeadLetterAsync(string reason, string description, CancellationToken cancellationToken) =>
            _args.DeadLetterMessageAsync(_args.Message,
                deadLetterReason: reason,
                deadLetterErrorDescription: description,
                cancellationToken: cancellationToken);
    }

    private Task OnProcessorErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "ServiceBusProcessor error. Source={Source} Entity={EntityPath}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }

    private static async Task<short> NextAttemptNumberAsync(
        IntegracionesAwDbContext db,
        Guid entidadId,
        CancellationToken cancellationToken)
    {
        var last = await db.Envios
            .Where(e => e.EntidadExternaId == entidadId)
            .Select(e => (short?)e.AttemptNumber)
            .OrderByDescending(n => n)
            .FirstOrDefaultAsync(cancellationToken);
        return (short)((last ?? 0) + 1);
    }
}
