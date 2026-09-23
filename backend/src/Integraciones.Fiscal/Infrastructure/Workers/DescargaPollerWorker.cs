using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Fiscal.Infrastructure.Workers;

/// <summary>
/// <c>BackgroundService</c> que avanza la FSM de las
/// <see cref="SolicitudDescarga"/> vivas y cosecha meta-items cuando
/// FiscalAPI termina de procesar (doc 02 §5.2).
///
/// <para>
/// Cada tick:
/// </para>
/// <list type="number">
///   <item>Toma un batch de solicitudes en estados no-terminales con
///   <c>next_poll_at &lt;= ahora</c> (índice filtrado
///   <c>ix_solicitudes_descarga_next_poll</c>).</item>
///   <item>Por cada solicitud:
///         <list type="bullet">
///           <item>Llama <see cref="IFiscalApiSdkClient.ConsultarSolicitudAsync"/>.</item>
///           <item>Mapea respuesta vía <see cref="SolicitudDescarga.AplicarPoll"/>
///           (FSM + backoff exponencial).</item>
///           <item>Si quedó en <see cref="EstadoSolicitudDescarga.Terminada"/>:
///           cosechar meta-items + XMLs (en paralelo lógico,
///           emparejados por UUID) y entregar al
///           <see cref="IFiscalCfdiReceiver"/>; al terminar marca
///           <see cref="EstadoSolicitudDescarga.Cosechada"/>.</item>
///         </list>
///   </item>
/// </list>
///
/// <para>
/// <b>Aislamiento de fallos</b>: cada solicitud se procesa en try/catch
/// independiente. Una solicitud rota no detiene el tick.
/// </para>
///
/// <para>
/// <b>Idempotencia</b>: la cosecha invoca al receiver por cada UUID;
/// el receiver es responsable de deduplicar. Si el worker se cae a
/// mitad de cosecha, al re-correr el meta-items handler los reentrega
/// (el receiver decide qué hacer).
/// </para>
/// </summary>
public sealed class DescargaPollerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<IntegracionesFiscalWorkerOptions> _options;
    private readonly ILogger<DescargaPollerWorker> _logger;

    public DescargaPollerWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<IntegracionesFiscalWorkerOptions> options,
        ILogger<DescargaPollerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.CurrentValue.Poller;
        if (opts.Disabled)
        {
            _logger.LogInformation("[DescargaPollerWorker] Disabled — loop no inicia.");
            return;
        }

        _logger.LogInformation(
            "[DescargaPollerWorker] Iniciado. TickInterval={Interval}s BatchSize={Batch} MaxBackoff={Backoff}m",
            opts.TickIntervalSeconds, opts.BatchSize, opts.MaxBackoffMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DescargaPollerWorker] Error en tick. Continuando.");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(opts.TickIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    /// <summary>Un tick. Internal para tests unit.</summary>
    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue.Poller;

        // Snapshot scope para el batch (bypass empresa).
        List<Guid> solicitudIds;
        using (var snapshot = _scopeFactory.CreateScope())
        {
            var db = snapshot.ServiceProvider.GetRequiredService<IntegracionesFiscalDbContext>();
            var originContext = snapshot.ServiceProvider.GetRequiredService<IAuditOriginContext>();
            using var origin = originContext.SetOrigin(nameof(DescargaPollerWorker));
            var empresaContext = snapshot.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
            using var bypass = empresaContext.Bypass();
            var clock = snapshot.ServiceProvider.GetRequiredService<IClock>();
            var ahora = clock.UtcNow;

            solicitudIds = await db.SolicitudesDescarga
                .AsNoTracking()
                .Where(s => (s.Estado == EstadoSolicitudDescarga.Pendiente
                          || s.Estado == EstadoSolicitudDescarga.EsperandoSat
                          || s.Estado == EstadoSolicitudDescarga.EsperandoApi
                          || s.Estado == EstadoSolicitudDescarga.Terminada)
                         && s.NextPollAt <= ahora)
                .OrderBy(s => s.NextPollAt)
                .Take(opts.BatchSize)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);
        }

        if (solicitudIds.Count == 0)
        {
            _logger.LogDebug("[DescargaPollerWorker] Sin solicitudes con next_poll_at vencido. Skip tick.");
            return;
        }

        foreach (var solicitudId in solicitudIds)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try { await ProcesarSolicitudAsync(solicitudId, opts, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[DescargaPollerWorker] solicitud={Id}: error procesando. Continuando.", solicitudId);
            }
        }
    }

    private async Task ProcesarSolicitudAsync(
        Guid solicitudId,
        IntegracionesFiscalWorkerOptions.PollerOptions opts,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntegracionesFiscalDbContext>();
        var sdk = scope.ServiceProvider.GetRequiredService<IFiscalApiSdkClient>();
        var receiver = scope.ServiceProvider.GetRequiredService<IFiscalCfdiReceiver>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var originContext = scope.ServiceProvider.GetRequiredService<IAuditOriginContext>();
        using var origin = originContext.SetOrigin(nameof(DescargaPollerWorker));
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        var solicitud = await db.SolicitudesDescarga
            .FirstOrDefaultAsync(s => s.Id == solicitudId, cancellationToken);
        if (solicitud is null) return;

        // Solicitud terminada pero aún no cosechada: brincamos directo a la cosecha.
        if (solicitud.Estado != EstadoSolicitudDescarga.Terminada)
        {
            SolicitudDescargaExternaDto consulta;
            try
            {
                consulta = await sdk.ConsultarSolicitudAsync(
                    solicitud.EmpresaId, solicitud.RequestIdExterno, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[DescargaPollerWorker] solicitud={Id}: error consultando. Marcando next_poll con backoff.",
                    solicitud.Id);
                solicitud.AplicarPoll(null, null, null, clock.UtcNow, opts.MaxBackoffMinutes);
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            solicitud.AplicarPoll(
                consulta.SatRequestStatusId,
                consulta.DownloadRequestStatusId,
                consulta.InvoiceCount,
                clock.UtcNow,
                opts.MaxBackoffMinutes);
            await db.SaveChangesAsync(cancellationToken);

            // Si quedó terminal de error o todavía no Terminada, fin del proc.
            if (solicitud.Estado != EstadoSolicitudDescarga.Terminada) return;
        }

        // Estado Terminada → cosechar meta-items y entregar al receiver.
        await CosecharAsync(db, sdk, receiver, clock, solicitud, opts, cancellationToken);
    }

    private async Task CosecharAsync(
        IntegracionesFiscalDbContext db,
        IFiscalApiSdkClient sdk,
        IFiscalCfdiReceiver receiver,
        IClock clock,
        SolicitudDescarga solicitud,
        IntegracionesFiscalWorkerOptions.PollerOptions opts,
        CancellationToken cancellationToken)
    {
        var rule = await db.DownloadRulesExternas
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == solicitud.DownloadRuleId, cancellationToken);
        var rfc = rule is null ? null : await db.RfcsReceptores
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == rule.RfcReceptorId, cancellationToken);
        if (rfc is null)
        {
            _logger.LogWarning(
                "[DescargaPollerWorker] solicitud={Id}: no se encontró RfcReceptor; no se cosecha.", solicitud.Id);
            solicitud.MarcarError("RFC_RECEPTOR_NOT_FOUND",
                "El RfcReceptor original fue eliminado o no es accesible.", clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        // PR-14 (D1 revisada): rule CFDI ⇒ cosechamos meta-items + XMLs y
        // los pareamos por UUID antes de entregar al receiver. Cargamos
        // XMLs en memoria primero (típicamente <500 por request diaria) y
        // luego streameamos meta-items.
        Dictionary<string, byte[]> xmlsPorUuid;
        try
        {
            xmlsPorUuid = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            await foreach (var xml in sdk.ListarXmlsAsync(
                solicitud.EmpresaId, solicitud.RequestIdExterno, cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested) break;
                xmlsPorUuid[xml.Uuid] = xml.XmlBytes;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[DescargaPollerWorker] solicitud={Id}: error cargando XMLs. Reintentaremos.", solicitud.Id);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var cosechados = 0;
        var sinXml = 0;
        try
        {
            await foreach (var item in sdk.ListarMetaItemsAsync(
                solicitud.EmpresaId, solicitud.RequestIdExterno, cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested) break;

                var uuidKey = item.Uuid?.Trim().ToUpperInvariant() ?? string.Empty;
                if (!xmlsPorUuid.TryGetValue(uuidKey, out var xmlBytes))
                {
                    sinXml++;
                    _logger.LogWarning(
                        "[DescargaPollerWorker] solicitud={Id}: meta-item UUID {Uuid} sin XML pareable — skip.",
                        solicitud.Id, item.Uuid);
                    continue;
                }

                var payload = new CfdiCosechadoPayload(
                    EmpresaId:             solicitud.EmpresaId,
                    RfcReceptorMillet:     rfc.Rfc,
                    Uuid:                  uuidKey,
                    RfcEmisor:             item.RfcEmisor,
                    NombreEmisor:          item.NombreEmisor,
                    RfcReceptor:           item.RfcReceptor,
                    NombreReceptor:        item.NombreReceptor,
                    FechaCfdi:             item.FechaCfdi,
                    FechaCertificacionSat: item.FechaCertificacionSat,
                    Total:                 item.Total,
                    TipoComprobante:       item.TipoComprobante,
                    EstatusSat:            item.EstatusSat,
                    FechaCancelacion:      item.FechaCancelacion,
                    SolicitudDescargaId:   solicitud.Id,
                    RequestIdExterno:      solicitud.RequestIdExterno,
                    XmlBytes:              xmlBytes);
                await receiver.IngresarCfdiAsync(payload, cancellationToken);
                cosechados++;

                if (cosechados % opts.CosechaChunkSize == 0)
                {
                    _logger.LogDebug(
                        "[DescargaPollerWorker] solicitud={Id}: cosechados {Count} hasta ahora...",
                        solicitud.Id, cosechados);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[DescargaPollerWorker] solicitud={Id}: error cosechando ({Cosechados} ya entregados).",
                solicitud.Id, cosechados);
            // No marcamos Error — la próxima corrida intentará seguir.
            // El receiver debe ser idempotente para reentregas.
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        solicitud.MarcarCosechada(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "[DescargaPollerWorker] solicitud={Id}: cosecha completa. {Count} CFDIs entregados, {SinXml} sin XML.",
            solicitud.Id, cosechados, sinXml);
    }
}
