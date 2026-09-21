using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.IntegrationEvents;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Domain.Ports.Blob;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Application.Workers;

/// <summary>
/// <see cref="IHostedService"/> que sincroniza los PDF que A+W exporta
/// (oferta_&lt;nro&gt;.pdf / pedido_&lt;nro&gt;.pdf) hacia el ERP.
///
/// <para>
/// <b>Flow del ciclo:</b>
/// <list type="number">
///   <item>Candidatas: entidades <c>Correlated</c> con <c>AwDocId</c>, aún
///         sin PDF adjunto (<c>PdfBlobUrl == null</c>), correlacionadas
///         dentro de la ventana <c>LookbackHours</c>.</item>
///   <item>Drena <c>GET /documents?since=windowStart</c> (paginado) vía
///         <see cref="IAwDocumentsReader"/>.</item>
///   <item>Empareja cada documento con una candidata por
///         <c>(tipo, aw_doc_id)</c> — <c>oferta→Cotizacion</c>,
///         <c>pedido→Pedido</c>.</item>
///   <item>Descarga el PDF, lo sube a Blob Storage
///         (<see cref="IAlmacenarBlobPort"/>), adjunta la URL a la entidad,
///         publica <see cref="AwDocumentoAdjuntado"/> y notifica al Agent
///         (SignalR + Soketi).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Idempotencia:</b> una entidad con <c>PdfBlobUrl != null</c> sale del
/// set de candidatas — el PDF se adjunta una sola vez. El emparejamiento
/// no depende de un cursor persistido: el filtro <c>PdfBlobUrl == null</c>
/// es la fuente de verdad.
/// </para>
///
/// <para>
/// Mismo patrón timer-based + singleton + IHostedService que
/// <see cref="AwLateReconciliationWorker"/>: no requiere Service Bus y el
/// health check ve la misma instancia que el host ejecuta.
/// </para>
/// </summary>
public sealed class AwDocumentSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IntegracionesAwOptions _options;
    private readonly DocumentSyncKick _kick;
    private readonly ILogger<AwDocumentSyncWorker> _logger;

    private volatile bool _running;

    /// <summary>True mientras el loop esté activo (health check).</summary>
    public bool IsRunning => _running;

    public AwDocumentSyncWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<IntegracionesAwOptions> options,
        DocumentSyncKick kick,
        ILogger<AwDocumentSyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _kick = kick;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idleInterval = TimeSpan.FromSeconds(_options.DocumentSync.IntervalSeconds);
        var fastInterval = TimeSpan.FromSeconds(_options.DocumentSync.FastIntervalSeconds);
        _running = true;
        _logger.LogInformation(
            "AwDocumentSyncWorker iniciado. idle={Idle}s fast={Fast}s fastWindow={FastWin}min lookback={LookbackH}h batch={Batch} maxPages={MaxPages}",
            idleInterval.TotalSeconds,
            fastInterval.TotalSeconds,
            _options.DocumentSync.FastWindowMinutes,
            _options.DocumentSync.LookbackHours,
            _options.DocumentSync.BatchSize,
            _options.DocumentSync.MaxPagesPerCycle);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var pendingHot = 0;
                try
                {
                    pendingHot = await SyncOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "AwDocumentSyncWorker: ciclo falló — se reanuda en el siguiente intervalo.");
                    IntegracionesAwMeter.DocumentSyncErrors.Add(1);
                }

                // Cadencia adaptativa: si hay PDF pendientes en la ventana
                // caliente, polea rápido hasta que aparezcan; si no, idle. Un
                // kick de correlación despierta antes del timeout (fetch por
                // evento).
                var delay = pendingHot > 0 ? fastInterval : idleInterval;
                try
                {
                    await _kick.WaitAsync(delay, stoppingToken);
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
            _logger.LogInformation("AwDocumentSyncWorker detenido.");
        }
    }

    /// <summary>
    /// Un ciclo de sincronización. <c>internal</c> para que los tests lo
    /// invoquen directo sin levantar el host. Devuelve cuántas cotizaciones
    /// correlacionadas sin PDF quedan dentro de la ventana caliente — el loop
    /// lo usa para decidir la cadencia (rápida vs idle).
    /// </summary>
    internal async Task<int> SyncOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();
        var originContext = sp.GetRequiredService<IAuditOriginContext>();
        using var origin = originContext.SetOrigin(nameof(AwDocumentSyncWorker));
        using var _ = empresaContext.Bypass();

        var db = sp.GetRequiredService<IntegracionesAwDbContext>();
        var reader = sp.GetRequiredService<IAwDocumentsReader>();
        var blob = sp.GetRequiredService<IAlmacenarBlobPort>();
        var publisher = sp.GetRequiredService<IIntegrationEventPublisher>();
        var notifier = sp.GetRequiredService<IIntegracionesAwNotifier>();
        var agentRealtime = sp.GetRequiredService<IAgentRealtimePublisher>();
        var agentWebhook = sp.GetRequiredService<IAgentDocumentoWebhook>();
        var clock = sp.GetRequiredService<IClock>();

        var now = clock.UtcNow;
        var windowStart = now.AddHours(-_options.DocumentSync.LookbackHours);

        // Doc-list-driven: el driver es la carpeta caliente del drop service,
        // que se autolimpia (los PDF tratados se mueven a archive\). Por cada
        // documento listado:
        //   - resolvemos su entidad por (tipo, aw_doc_id);
        //   - sin entidad → lo dejamos (correlacionará/aparecerá luego);
        //   - sin PDF → descargamos, subimos a blob, adjuntamos;
        //   - con o sin subida, lo ARCHIVAMOS (ack) para sacarlo de la caliente.
        // El archive solo ocurre tras subir+persistir con éxito → at-least-once:
        // si algo falla antes del ack, el PDF sigue en la caliente y se reintenta.
        DateTimeOffset? since = windowStart;
        var pages = 0;
        var adjuntados = 0;
        var archivados = 0;

        while (pages < _options.DocumentSync.MaxPagesPerCycle
               && !cancellationToken.IsCancellationRequested)
        {
            var page = await reader.ListSinceAsync(
                since, _options.DocumentSync.BatchSize, cancellationToken);
            pages++;

            if (page.Documents.Count == 0) break;

            // Resolver entidades por aw_doc_id de la página. SIN filtro de PDF:
            // necesitamos tanto las pendientes (subir) como las ya tratadas
            // (re-archivar si se perdió un ack previo).
            var awDocIds = page.Documents.Select(d => d.AwDocId).Distinct().ToList();
            var entidades = await db.EntidadesExternas
                .Where(e => e.AwDocId != null && awDocIds.Contains(e.AwDocId.Value))
                .ToListAsync(cancellationToken);
            // Match SOLO por aw_doc_id. El ERP guarda toda entidad como
            // Cotizacion (RegistrarCotizacionEdi no crea Pedido), así que el
            // doc_type del PDF (oferta/pedido) NO corresponde al TipoEntidad —
            // emparejar por tipo dejaba los pedidos sin adjuntar. El aw_doc_id
            // es único en A+W → identifica la entidad sin ambigüedad.
            var byAwDocId = entidades
                .GroupBy(e => e.AwDocId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var doc in page.Documents)
            {
                if (!byAwDocId.TryGetValue(doc.AwDocId, out var entidad)) continue;

                try
                {
                    if (entidad.PdfBlobUrl is null)
                    {
                        await AdjuntarPdfAsync(
                            db, reader, blob, publisher, notifier, agentRealtime, agentWebhook,
                            entidad, doc, now, cancellationToken);
                        adjuntados++;
                    }

                    // Tratado (recién subido o ya estaba): sacarlo de la carpeta
                    // caliente. Best-effort: si el ack falla, próximo ciclo
                    // re-archiva (entidad ya tiene PdfBlobUrl → no re-sube).
                    await reader.ArchivarAsync(doc.Filename, cancellationToken);
                    archivados++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "AwDocumentSyncWorker: falló procesar/archivar PDF. aggregate={Id} filename={Filename}",
                        entidad.Id, doc.Filename);
                    IntegracionesAwMeter.DocumentSyncErrors.Add(1);
                }
            }

            if (page.NextSince is null) break;
            since = page.NextSince;
        }

        if (adjuntados > 0 || archivados > 0)
        {
            _logger.LogInformation(
                "AwDocumentSyncWorker ciclo. adjuntados={Adj} archivados={Arch} paginas={Pages}",
                adjuntados, archivados, pages);
        }

        // Cotizaciones correlacionadas hace poco y aún sin PDF: mantienen al
        // worker en cadencia rápida (el PDF de A+W es inminente). Pasada la
        // ventana, dejan de contar → el worker relaja a idle y el barrido por
        // LookbackHours las sigue cubriendo.
        var hotWindowStart = now.AddMinutes(-_options.DocumentSync.FastWindowMinutes);
        var pendingHot = await db.EntidadesExternas.CountAsync(
            e => e.Estado == EstadoEntidad.Correlated
                 && e.AwDocId != null
                 && e.PdfBlobUrl == null
                 && e.CorrelatedAt != null
                 && e.CorrelatedAt > hotWindowStart,
            cancellationToken);

        return pendingHot;
    }

    private static async Task AdjuntarPdfAsync(
        IntegracionesAwDbContext db,
        IAwDocumentsReader reader,
        IAlmacenarBlobPort blob,
        IIntegrationEventPublisher publisher,
        IIntegracionesAwNotifier notifier,
        IAgentRealtimePublisher agentRealtime,
        IAgentDocumentoWebhook agentWebhook,
        EntidadExterna entidad,
        AwDocumentItem doc,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Descarga + sube. El stream se dispone tras subirlo (cierra la
        // conexión HTTP al drop service).
        string blobUrl;
        await using (var stream = await reader.DownloadAsync(doc.Filename, cancellationToken))
        {
            blobUrl = await blob.SubirAsync(
                entidad.Id, stream, "application/pdf", doc.Filename, cancellationToken);
        }

        // pdf_uploaded_at = momento REAL en que A+W exportó el PDF
        // (modified_at del archivo), NO el de correlación ni `now`. Permite
        // medir el tiempo correlación→entrega del PDF. Guard contra un
        // modified_at ausente/malformado (MinValue) del drop service.
        var exportadoEn = doc.ModifiedAt > DateTimeOffset.MinValue ? doc.ModifiedAt : now;

        entidad.AdjuntarDocumentoPdf(blobUrl, doc.Filename, exportadoEn);

        await publisher.PublishAsync(
            new AwDocumentoAdjuntado(
                EmpresaId: entidad.EmpresaId,
                OcurridoEn: now,
                AggregateId: entidad.Id,
                QuoteReference: entidad.ReferenciaExterna,
                AwDocId: doc.AwDocId,
                DocType: doc.DocType,
                PdfFilename: doc.Filename,
                PdfUploadedAt: exportadoEn),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        // Notificaciones best-effort (post-persistencia).
        await notifier.NotifyDocumentoAdjuntadoAsync(
            empresaId: entidad.EmpresaId,
            aggregateId: entidad.Id,
            quoteReference: entidad.ReferenciaExterna,
            awDocId: doc.AwDocId,
            docType: doc.DocType,
            pdfFilename: doc.Filename,
            pdfUploadedAt: exportadoEn,
            cancellationToken: cancellationToken);

        await agentRealtime.PublishCotizacionActualizadaAsync(entidad, cancellationToken);

        // Webhook server-to-server al Agent (entrega "live" sin depender del
        // browser). Best-effort — la impl no lanza; el cron del Agent es el
        // respaldo. Se dispara una sola vez (solo entramos acá con PdfBlobUrl null).
        await agentWebhook.NotifyPdfListoAsync(entidad, cancellationToken);

        IntegracionesAwMeter.DocumentoAdjuntado.Add(1);
    }
}
