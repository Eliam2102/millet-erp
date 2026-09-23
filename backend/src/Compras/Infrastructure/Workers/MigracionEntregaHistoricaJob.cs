using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Compras.Application.MigracionEntrega;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Infrastructure.Workers;

/// <summary>
/// Job <b>one-shot</b> de migración de históricas (ADR-0043 PR #4). Reconstruye
/// el acumulador <c>CantidadEntregada</c> de las RQ previas a la conmutación y
/// reclasifica las que quedaron <c>Cerrada</c> sin estar realmente entregadas.
///
/// <para>
/// Corre una sola vez al arranque, atado a config (no es un loop). Pasos:
/// <list type="number">
///   <item>Carga candidatas: <c>Cerrada</c> (reclasifica + backfill) y
///     <c>EnSurtido</c> (solo backfill de <c>cant_entregada</c>). Las terminales
///     del #400 (<c>CerradaSinSurtir</c>/<c>CerradaSurtidaParcial</c>) y
///     <c>Cancelada</c>/<c>Rechazada</c>/<c>Eliminada</c> NUNCA entran.</item>
///   <item>Lee el entregado de Almacén (puerto) y clasifica cada RQ con
///     <see cref="ReclasificacionEntregaCalculator"/>: EXACTA (se migra) o
///     AMBIGUA (se deja para revisión manual; NO se toca).</item>
///   <item>Aplica vía <c>UPDATE</c> dirigido en TX por RQ (los setters del
///     agregado son privados; un backfill one-time va por SQL — mismo criterio
///     que <c>IdempotencyKeysCleanupJob</c>). <c>cant_entregada</c> se escribe
///     con <b>SET</b> (no incrementa) y capado al techo por el clasificador.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Idempotente sin tabla marcador:</b> SET (no incrementa) + filtro de estado.
/// La reclasificación solo toca <c>Cerrada</c>; una RQ ya movida está en
/// <c>EnSurtido</c> y no reentra al cubo de reclasificación (solo recibe el
/// mismo SET). Re-correr es inocuo y salta las ya movidas. <b>Auditoría</b> =
/// log estructurado (App Insights), una línea por corrida + por RQ ambigua.
/// </para>
///
/// <para>
/// <b>Runbook:</b> tras una corrida REAL exitosa, poner
/// <c>Migraciones:EntregaHistorica:Enabled=false</c> para que un redeploy no
/// re-arme el job. Ver <see cref="MigracionEntregaHistoricaOptions"/>.
/// </para>
/// </summary>
public sealed class MigracionEntregaHistoricaJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MigracionEntregaHistoricaOptions _options;
    private readonly ILogger<MigracionEntregaHistoricaJob> _logger;

    public MigracionEntregaHistoricaJob(
        IServiceScopeFactory scopeFactory,
        IOptions<MigracionEntregaHistoricaOptions> options,
        ILogger<MigracionEntregaHistoricaJob> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "MigracionEntregaHistoricaJob deshabilitado (Migraciones:EntregaHistorica:Enabled=false). No-op.");
            return;
        }

        try
        {
            if (_options.DryRun)
            {
                _logger.LogWarning(
                    "MigracionEntregaHistoricaJob: arrancando en DRY-RUN (no escribe nada). Para aplicar de verdad: DryRun=false.");
            }
            else
            {
                _logger.LogWarning(
                    "⚠️ MIGRACIÓN REAL: MigracionEntregaHistoricaJob va a APLICAR reclasificación de históricas (NO dry-run). "
                    + "Tras terminar, RECUERDA poner Migraciones:EntregaHistorica:Enabled=false (runbook).");
            }

            var reporte = await EjecutarAsync(_options.DryRun, stoppingToken);

            if (_options.DryRun)
            {
                _logger.LogWarning(
                    "MigracionEntregaHistoricaJob DRY-RUN (no escribió): movería {Movidas} Cerradas→EnSurtido, "
                    + "{Quedan} quedan Cerradas, {Backfill} EnSurtido solo-backfill, {Lineas} líneas; {Ambiguas} AMBIGUAS (revisión manual).",
                    reporte.Movidas, reporte.QuedanCerradas, reporte.BackfillEnSurtido,
                    reporte.LineasBackfilleadas, reporte.Ambiguas.Count);
            }
            else
            {
                _logger.LogWarning(
                    "✅ MIGRACIÓN REAL APLICADA: {Movidas} Cerradas→EnSurtido, {Quedan} quedan Cerradas, "
                    + "{Backfill} EnSurtido solo-backfill, {Lineas} líneas backfilleadas, {Ambiguas} AMBIGUAS sin tocar. "
                    + "RECUERDA: poner Migraciones:EntregaHistorica:Enabled=false para que un redeploy no re-arme el job.",
                    reporte.Movidas, reporte.QuedanCerradas, reporte.BackfillEnSurtido,
                    reporte.LineasBackfilleadas, reporte.Ambiguas.Count);
            }

            foreach (var amb in reporte.Ambiguas)
            {
                _logger.LogWarning(
                    "[MigracionEntregaHistorica] AMBIGUA (revisión manual, NO tocada): RQ {Folio} ({RqId}) "
                    + "— artículo repetido con salida sin LineaRqId; el reparto por línea es indecidible.",
                    amb.Folio, amb.RqId);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "MigracionEntregaHistoricaJob falló. Aplica por RQ en TX independientes; re-correr es idempotente.");
        }
    }

    /// <summary>
    /// Una corrida sobre todas las candidatas. <c>internal</c> para tests.
    /// </summary>
    internal Task<ReporteMigracion> EjecutarAsync(bool dryRun, CancellationToken ct)
        => EjecutarAsync(dryRun, rqIdsOverride: null, ct);

    /// <summary>
    /// Una corrida acotada a <paramref name="rqIdsOverride"/> (tests). El
    /// override <b>se intersecta</b> con los estados elegibles — jamás amplía el
    /// alcance ni toca terminales.
    /// </summary>
    internal async Task<ReporteMigracion> EjecutarAsync(
        bool dryRun, IReadOnlyCollection<Guid>? rqIdsOverride, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var originContext = sp.GetRequiredService<IAuditOriginContext>();
        using var origin = originContext.SetOrigin(nameof(MigracionEntregaHistoricaJob));
        var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        var db = sp.GetRequiredService<ComprasDbContext>();
        var port = sp.GetRequiredService<IAlmacenEntregasReadPort>();

        // Solo Cerrada (reclasifica+backfill) y EnSurtido (solo backfill).
        var query = db.Requisiciones
            .AsNoTracking()
            .Include(r => r.Lineas)
            .Where(r => r.Estado == EstadoRequisicion.Cerrada
                     || r.Estado == EstadoRequisicion.EnSurtido);

        if (rqIdsOverride is not null)
        {
            var ids = rqIdsOverride.ToArray();
            query = query.Where(r => ids.Contains(r.Id));
        }

        var rqs = await query.ToListAsync(ct);

        var entregaPorRq = await port.ObtenerEntregaParaReclasificacionAsync(
            rqs.Select(r => r.Id).ToList(), ct);

        var vacia = new EntregaReclasificacion(
            new Dictionary<Guid, decimal>(), new Dictionary<Guid, decimal>());

        var movidas = 0;
        var quedan = 0;
        var backfillEnSurtido = 0;
        var lineasBackfilleadas = 0;
        var ambiguas = new List<RqAmbigua>();

        foreach (var rq in rqs)
        {
            var lineas = rq.Lineas
                .Select(l => new LineaParaReclasificar(l.Id, l.ArticuloId, l.Cantidad))
                .ToList();
            var entrega = entregaPorRq.GetValueOrDefault(rq.Id) ?? vacia;

            var resultado = ReclasificacionEntregaCalculator.Clasificar(lineas, entrega);

            if (resultado.EsAmbigua)
            {
                ambiguas.Add(new RqAmbigua(rq.Id, rq.Folio.Valor));
                continue;
            }

            var moverAEnSurtido = rq.Estado == EstadoRequisicion.Cerrada
                                  && !resultado.TotalmenteEntregada;

            if (!dryRun)
            {
                await AplicarAsync(db, rq.Id, resultado.EntregadoPorLinea, moverAEnSurtido, ct);
            }

            lineasBackfilleadas += resultado.EntregadoPorLinea.Count;
            if (rq.Estado == EstadoRequisicion.EnSurtido)
            {
                backfillEnSurtido++;
            }
            else if (moverAEnSurtido)
            {
                movidas++;
            }
            else
            {
                quedan++;
            }
        }

        return new ReporteMigracion
        {
            DryRun = dryRun,
            Movidas = movidas,
            QuedanCerradas = quedan,
            BackfillEnSurtido = backfillEnSurtido,
            LineasBackfilleadas = lineasBackfilleadas,
            Ambiguas = ambiguas,
        };
    }

    /// <summary>
    /// Aplica el veredicto de una RQ en una TX: backfill de <c>cant_entregada</c>
    /// por línea (SET, capado por el clasificador) y, si procede, el cambio de
    /// estado <c>Cerrada → EnSurtido</c>.
    /// </summary>
    private static async Task AplicarAsync(
        ComprasDbContext db,
        Guid rqId,
        IReadOnlyDictionary<Guid, decimal> entregadoPorLinea,
        bool moverAEnSurtido,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        foreach (var (lineaId, valor) in entregadoPorLinea)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE compras.requisicion_lineas SET cant_entregada = {valor} WHERE id = {lineaId}",
                ct);
        }

        if (moverAEnSurtido)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE compras.requisiciones SET estado = {(short)EstadoRequisicion.EnSurtido} WHERE id = {rqId}",
                ct);
        }

        await tx.CommitAsync(ct);
    }
}
