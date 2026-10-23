using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.CuentasPorPagar.Application.FacturaProveedor.Revision;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Notificaciones;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Calendario;

namespace Millet.CuentasPorPagar.Infrastructure.Workers;

/// <summary>
/// Worker en-proceso (ADR-0022, §A22 del 01-diseno) que detecta
/// facturas en revisión cuyo tiempo desde
/// <see cref="FacturaProveedor.FechaEntradaRevision"/> cumple los
/// thresholds del SLA del motivo, y emite notificaciones vía
/// <see cref="INotificacionService"/>.
///
/// <para>
/// **Thresholds por SLA** (en días hábiles con <see cref="ICalendarioHabil"/>;
/// el día de entrada no cuenta y la fecha es la local de México, ver
/// <see cref="PlazoRevision"/>):
/// <list type="bullet">
///   <item>SLA 5 días: aviso día 3, SLA vencido día 5, escalamiento
///         a Dirección de Finanzas día 10.</item>
///   <item>SLA 15 días (Disputa con proveedor): aviso día 8, vencido
///         día 12, escalamiento día 20.</item>
///   <item>SLA null (Indicación expresa): no notifica — libera el
///         solicitante.</item>
/// </list>
/// </para>
///
/// <para>
/// **Idempotencia**: cada aviso se persiste en
/// <c>cuentas_por_pagar.avisos_sla_revision</c> con índice único
/// (factura, nivel, día). Correr el worker dos veces el mismo día no duplica
/// el aviso. El aviso nunca cambia el estado de la factura ni libera el pago.
/// El envío lo resuelve la implementación de <see cref="INotificacionService"/>
/// registrada (log o correo Graph).
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
        var calendario = sp.GetRequiredService<ICalendarioHabil>();

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
                MotivoNombre = m.Nombre,
                AvisaCompras = m.DependenciaRevisoraDefaultCodigo == "COMPRAS" || m.AreaAvisadaCodigo == "COMPRAS",
                f.SerieProveedor,
                f.FolioProveedor,
                FechaEntrada = f.FechaEntradaRevision!.Value,
                DependenciaId = f.DependenciaRevisoraId!.Value,
            }).ToListAsync(cancellationToken);

        var hoy = FechaContable.HoyLocal(ahora);
        var cache = new Dictionary<DateOnly, int>();
        var enviadas = 0;
        foreach (var f in enRevision)
        {
            if (f.SlaDias is not int sla) continue; // motivo sin SLA (Indicación expresa)

            var dias = await PlazoRevision.DiasHabilesAsync(calendario, f.FechaEntrada, ahora, cache, cancellationToken);
            if (PlazoRevision.Nivel(dias, sla) is not NivelEscalamientoSla nivel) continue;

            // Deduplicación (factura, nivel, día local): un aviso por día y nivel.
            if (await db.AvisosSlaRevision.AnyAsync(
                    a => a.FacturaProveedorId == f.Id && a.Nivel == nivel && a.Fecha == hoy,
                    cancellationToken))
                continue;

            db.AvisosSlaRevision.Add(new AvisoSlaRevision(
                f.EmpresaId, f.Id, f.DependenciaId, f.MotivoId, nivel, hoy, dias, sla, f.FechaEntrada, ahora));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
            {
                // Otra instancia ya emitió ese aviso.
                db.ChangeTracker.Clear();
                continue;
            }

            try
            {
                await notif.NotificarSlaRevisionAsync(new SlaRevisionNotificacion(
                    EmpresaId: f.EmpresaId,
                    FacturaProveedorId: f.Id,
                    DependenciaRevisoraId: f.DependenciaId,
                    MotivoRevisionId: f.MotivoId,
                    DiasEnRevision: dias,
                    SlaDias: sla,
                    Nivel: nivel,
                    Folio: string.Concat(f.SerieProveedor, f.FolioProveedor) is { Length: > 0 } folio ? folio : null,
                    MotivoNombre: f.MotivoNombre,
                    AvisaCompras: f.AvisaCompras), cancellationToken);
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
