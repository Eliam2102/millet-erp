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
/// <c>BackgroundService</c> que materializa solicitudes de descarga
/// masiva contra FiscalAPI (doc 02 §5.1). Cada tick:
/// <list type="number">
///   <item>Itera <see cref="ConfiguracionPac"/> activas (todas las empresas).</item>
///   <item>Por cada <see cref="RfcReceptor"/> con descarga habilitada Y FIEL vigente:
///         <list type="bullet">
///           <item>Asegura la <see cref="DownloadRuleExterna"/>
///           (Recibidos × Metadata × Todos) — crea en FiscalAPI si no existe.</item>
///           <item>Por cada día en
///           <c>[hoy - BackfillDays, hoy)</c> que aún no tenga solicitud:
///           crea <see cref="SolicitudDescarga"/> + POST a FiscalAPI.</item>
///         </list>
///   </item>
/// </list>
///
/// <para>
/// <b>Idempotente</b>: el constraint
/// <c>uq_solicitudes_request_externo</c> garantiza que el ID de
/// FiscalAPI no se duplique; el pre-check por
/// <c>(downloadRuleId, startDate)</c> evita llamar al PAC dos veces
/// para la misma combinación.
/// </para>
///
/// <para>
/// <b>Multi-tenant</b>: levanta <c>ICurrentEmpresaContext.Bypass()</c>
/// como el worker viejo para iterar todas las empresas, y vuelve a
/// "modo empresa" cuando llama al SDK (el adapter resuelve credenciales
/// por <c>empresaId</c> explícito).
/// </para>
/// </summary>
public sealed class DescargaSubmitterWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<IntegracionesFiscalWorkerOptions> _options;
    private readonly ILogger<DescargaSubmitterWorker> _logger;

    public DescargaSubmitterWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<IntegracionesFiscalWorkerOptions> options,
        ILogger<DescargaSubmitterWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.CurrentValue.Submitter;
        if (opts.Disabled)
        {
            _logger.LogInformation("[DescargaSubmitterWorker] Disabled — loop no inicia.");
            return;
        }

        _logger.LogInformation(
            "[DescargaSubmitterWorker] Iniciado. TickInterval={Interval}s BackfillDays={Backfill} MaxDaysPerRequest={MaxDays}",
            opts.TickIntervalSeconds, opts.BackfillDays, opts.MaxDaysPerRequest);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DescargaSubmitterWorker] Error en tick. Continuando.");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(opts.TickIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    /// <summary>Un tick. Internal para tests unit.</summary>
    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue.Submitter;

        // Snapshot scope para listar configuraciones activas (bypass empresa).
        List<Guid> empresasActivas;
        using (var snapshot = _scopeFactory.CreateScope())
        {
            var db = snapshot.ServiceProvider.GetRequiredService<IntegracionesFiscalDbContext>();
            var originContext = snapshot.ServiceProvider.GetRequiredService<IAuditOriginContext>();
            using var origin = originContext.SetOrigin(nameof(DescargaSubmitterWorker));
            var empresaContext = snapshot.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
            using var bypass = empresaContext.Bypass();
            empresasActivas = await db.ConfiguracionesPac
                .AsNoTracking()
                .Where(c => c.Activo)
                .Select(c => c.EmpresaId)
                .ToListAsync(cancellationToken);
        }

        if (empresasActivas.Count == 0)
        {
            _logger.LogDebug("[DescargaSubmitterWorker] Sin configuraciones activas. Skip tick.");
            return;
        }

        foreach (var empresaId in empresasActivas)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try { await ProcesarEmpresaAsync(empresaId, opts, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[DescargaSubmitterWorker] Error procesando empresa={Empresa}. Continuando.", empresaId);
            }
        }
    }

    private async Task ProcesarEmpresaAsync(
        Guid empresaId,
        IntegracionesFiscalWorkerOptions.SubmitterOptions opts,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntegracionesFiscalDbContext>();
        var sdk = scope.ServiceProvider.GetRequiredService<IFiscalApiSdkClient>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var originContext = scope.ServiceProvider.GetRequiredService<IAuditOriginContext>();
        using var origin = originContext.SetOrigin(nameof(DescargaSubmitterWorker));
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        var ahora = clock.UtcNow;

        var rfcsHabilitados = await db.RfcsReceptores
            .Where(r => r.EmpresaId == empresaId
                     && r.DescargaHabilitada
                     && r.FielValidFrom != null
                     && r.FielValidTo > ahora)
            .ToListAsync(cancellationToken);

        if (rfcsHabilitados.Count == 0)
        {
            _logger.LogDebug("[DescargaSubmitterWorker] empresa={Empresa}: sin RFCs habilitados con FIEL vigente.", empresaId);
            return;
        }

        foreach (var rfc in rfcsHabilitados)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try { await ProcesarRfcAsync(db, sdk, empresaId, rfc, opts, ahora, cancellationToken); }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[DescargaSubmitterWorker] empresa={Empresa} rfc={Rfc}: error. Continuando.", empresaId, rfc.Rfc);
            }
        }
    }

    private async Task ProcesarRfcAsync(
        IntegracionesFiscalDbContext db,
        IFiscalApiSdkClient sdk,
        Guid empresaId,
        RfcReceptor rfc,
        IntegracionesFiscalWorkerOptions.SubmitterOptions opts,
        DateTimeOffset ahora,
        CancellationToken cancellationToken)
    {
        // 1) Asegurar DownloadRule local + en FiscalAPI.
        var rule = await AsegurarRuleAsync(db, sdk, empresaId, rfc, ahora, cancellationToken);
        if (rule is null) return;

        // 2) Para cada bloque de días en [hoy - backfillDays, hoy):
        //    crear solicitud si no existe ya.
        var hoy = new DateTimeOffset(ahora.Year, ahora.Month, ahora.Day, 0, 0, 0, TimeSpan.Zero);
        var inicio = hoy.AddDays(-opts.BackfillDays);

        for (var dia = inicio; dia < hoy; dia = dia.AddDays(opts.MaxDaysPerRequest))
        {
            var ventanaFin = dia.AddDays(opts.MaxDaysPerRequest);
            if (ventanaFin > hoy) ventanaFin = hoy;

            var yaExiste = await db.SolicitudesDescarga
                .AsNoTracking()
                .AnyAsync(s => s.DownloadRuleId == rule.Id
                            && s.StartDate == dia
                            && s.Estado != EstadoSolicitudDescarga.Error,
                    cancellationToken);
            if (yaExiste) continue;

            SolicitudDescargaExternaDto creada;
            try
            {
                creada = await sdk.CrearSolicitudAsync(empresaId, rule.RuleIdExterno, dia, ventanaFin, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[DescargaSubmitterWorker] empresa={Empresa} rfc={Rfc} dia={Dia}: falla al crear solicitud — skip.",
                    empresaId, rfc.Rfc, dia);
                continue;
            }

            var solicitud = new SolicitudDescarga(
                id:                Guid.CreateVersion7(),
                empresaId:         empresaId,
                downloadRuleId:    rule.Id,
                requestIdExterno:  creada.IdExterno,
                startDate:         dia,
                endDate:           ventanaFin,
                ahora:             ahora);

            db.SolicitudesDescarga.Add(solicitud);
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "[DescargaSubmitterWorker] empresa={Empresa} rfc={Rfc}: solicitud creada {SolicitudId} ventana {Inicio} -> {Fin}.",
                empresaId, rfc.Rfc, solicitud.Id, dia, ventanaFin);
        }
    }

    private async Task<DownloadRuleExterna?> AsegurarRuleAsync(
        IntegracionesFiscalDbContext db,
        IFiscalApiSdkClient sdk,
        Guid empresaId,
        RfcReceptor rfc,
        DateTimeOffset ahora,
        CancellationToken cancellationToken)
    {
        // Regla por default: Recibidos × CFDI × Todos (Vigentes +
        // Cancelados con una sola rule — doc 02 §13.3 implicación 2).
        // PR-14 (D1 revisada §13.10): `CFDI` masivo, no `Metadata`. El
        // SDK no expone XML-por-UUID, así que descargar todo de una vez
        // es más simple y equivalente en costo (FiscalAPI cobra por
        // suscripción, no por solicitud).
        var existing = await db.DownloadRulesExternas
            .FirstOrDefaultAsync(r => r.RfcReceptorId == rfc.Id
                                   && r.SatQueryType == SatQueryType.Cfdi
                                   && r.DownloadType == DownloadType.Recibidos
                                   && r.SatInvoiceStatus == SatInvoiceStatusFilter.Todos,
                cancellationToken);
        if (existing is { Activa: true }) return existing;

        if (string.IsNullOrWhiteSpace(rfc.PersonIdExterno))
        {
            _logger.LogWarning(
                "[DescargaSubmitterWorker] empresa={Empresa} rfc={Rfc}: sin PersonIdExterno — saltando (debe subir FIEL primero).",
                empresaId, rfc.Rfc);
            return null;
        }

        DownloadRuleExternaDto creada;
        try
        {
            creada = await sdk.AsegurarDownloadRuleAsync(
                empresaId,
                rfc.PersonIdExterno,
                SatQueryType.Cfdi,
                DownloadType.Recibidos,
                SatInvoiceStatusFilter.Todos,
                descripcion: $"Millet — Recibidos CFDI {rfc.Rfc}",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[DescargaSubmitterWorker] empresa={Empresa} rfc={Rfc}: falla al crear rule en FiscalAPI.",
                empresaId, rfc.Rfc);
            return null;
        }

        if (existing is null)
        {
            existing = new DownloadRuleExterna(
                id:               Guid.CreateVersion7(),
                empresaId:        empresaId,
                rfcReceptorId:    rfc.Id,
                ruleIdExterno:    creada.IdExterno,
                satQueryType:     SatQueryType.Cfdi,
                downloadType:     DownloadType.Recibidos,
                satInvoiceStatus: SatInvoiceStatusFilter.Todos);
            db.DownloadRulesExternas.Add(existing);
        }
        else
        {
            existing.Activar();
        }
        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }
}
