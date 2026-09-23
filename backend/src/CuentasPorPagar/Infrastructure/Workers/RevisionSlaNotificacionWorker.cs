using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Notificaciones;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Infrastructure.Workers;

/// <summary>
/// Worker en-proceso (ADR-0022, §A22 del 01-diseno) que detecta
/// facturas en revisión cuyo tiempo desde
/// <see cref="FacturaProveedor.FechaEntradaRevision"/> cumple los
/// thresholds del SLA del motivo, y emite notificaciones vía
/// <see cref="INotificacionService"/>.
///
/// <para>
/// **Thresholds por SLA**:
/// <list type="bullet">
///   <item>SLA 5 días: aviso día 3, SLA vencido día 5, escalamiento
///         director día 10.</item>
///   <item>SLA 15 días (Disputa contractual): aviso día 8, vencido
///         día 12, escalamiento día 20.</item>
///   <item>SLA null (Indicación expresa): no notifica — libera el
///         solicitante.</item>
/// </list>
/// </para>
///
/// <para>
/// **Idempotencia simplificada en MVP**: el worker no persiste un
/// historial de notificaciones enviadas. Si corre 2 veces el mismo
/// día emite la notificación 2 veces. PLATFORM-TODO
/// (&lt;NotificacionesIdempotency&gt;): cuando el módulo
/// Notificaciones real exista, deduplica por
/// <c>(factura_id, nivel, dia)</c>.
/// </para>
/// </summary>
public sealed class RevisionSlaNotificacionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<RevisionSlaNotificacionOptions> _options;
    private readonly ILogger<RevisionSlaNotificacionWorker> _logger;

    public RevisionSlaNotificacionWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<RevisionSlaNotificacionOptions> options,
        ILogger<RevisionSlaNotificacionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.CurrentValue;
        if (opts.Disabled)
        {
            _logger.LogInformation("[RevisionSlaNotificacionWorker] Disabled — loop no inicia.");
            return;
        }

        _logger.LogInformation(
            "[RevisionSlaNotificacionWorker] Iniciado. Interval={Interval}s.",
            opts.IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RevisionSlaNotificacionWorker] Error inesperado.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    /// <summary>Internal para tests.</summary>
    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<CuentasPorPagarDbContext>();
        var notif = sp.GetRequiredService<INotificacionService>();
        var clock = sp.GetRequiredService<IClock>();
        var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();

        var originContext = sp.GetRequiredService<IAuditOriginContext>();
        using var origin = originContext.SetOrigin(nameof(RevisionSlaNotificacionWorker));
        using var bypass = empresaContext.Bypass();

        // Cargo facturas en revisión + su motivo (para conocer SlaDias).
        var ahora = clock.UtcNow;
        var enRevision = await (
            from f in db.FacturasProveedor.AsNoTracking()
            where f.EnRevision && f.MotivoRevisionId != null && f.FechaEntradaRevision != null
            join m in db.MotivosRevision.AsNoTracking() on f.MotivoRevisionId equals m.Id
            select new
            {
                f.Id,
                f.EmpresaId,
                MotivoId = m.Id,
                m.SlaDias,
                FechaEntrada = f.FechaEntradaRevision!.Value,
                DependenciaId = f.DependenciaRevisoraId!.Value,
            }).ToListAsync(cancellationToken);

        var enviadas = 0;
        foreach (var f in enRevision)
        {
            if (f.SlaDias is not int sla) continue; // motivo sin SLA (Indicación expresa)

            var dias = (int)Math.Floor((ahora - f.FechaEntrada).TotalDays);
            if (dias <= 0) continue;

            // Resuelve umbrales 3/5/10 (SLA 5) o 8/12/20 (SLA 15) — escala
            // lineal: aviso = 60% del SLA, vencido = 100%, escalamiento = 200%.
            var (avisoDia, vencidoDia, escalamientoDia) = sla switch
            {
                5  => (3, 5, 10),
                15 => (8, 12, 20),
                _  => ((int)Math.Ceiling(sla * 0.6m), sla, sla * 2),
            };

            NivelEscalamientoSla? nivel = dias switch
            {
                _ when dias >= escalamientoDia => NivelEscalamientoSla.EscalamientoDirector,
                _ when dias >= vencidoDia      => NivelEscalamientoSla.SlaVencido,
                _ when dias >= avisoDia        => NivelEscalamientoSla.AvisoGerente,
                _ => null,
            };

            if (nivel is null) continue;

            try
            {
                await notif.NotificarSlaRevisionAsync(new SlaRevisionNotificacion(
                    EmpresaId: f.EmpresaId,
                    FacturaProveedorId: f.Id,
                    DependenciaRevisoraId: f.DependenciaId,
                    MotivoRevisionId: f.MotivoId,
                    DiasEnRevision: dias,
                    SlaDias: sla,
                    Nivel: nivel.Value), cancellationToken);
                enviadas++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[RevisionSlaNotificacionWorker] Notificación falló para factura {Factura} nivel {Nivel}",
                    f.Id, nivel);
            }
        }

        _logger.LogInformation(
            "[RevisionSlaNotificacionWorker] Tick: {Total} en revisión, {Enviadas} notificaciones emitidas.",
            enRevision.Count, enviadas);
    }
}

public sealed class RevisionSlaNotificacionOptions
{
    public const string SectionName = "CuentasPorPagar:Workers:RevisionSla";

    public bool Disabled { get; init; }

    /// <summary>Default 8 AM = 28800s desde medianoche. En MVP corre cada
    /// intervalo regular sin sincronizar con reloj — simplemente diario.</summary>
    public int IntervalSeconds { get; init; } = 24 * 60 * 60;
}
