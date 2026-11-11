using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Contabilidad.Application.Periodos;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain.Audit;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Contabilidad.Infrastructure.Workers;

/// <summary>
/// Cierra por vencimiento los periodos con reapertura acotada vencida (M-213): periodo a <c>Cerrado</c>, reapertura concluida y la
/// transición en la bitácora (usuario «sistema»). Compara con la hora del servidor (<see cref="IClock"/>). El candado de periodo ya
/// trata la reapertura vencida como cerrada sin esperar a este proceso; esto solo formaliza el estado. Idempotente: la reapertura
/// concluida no se vuelve a tomar. Corre sin request (Bypass de empresa) con el mismo candado que cerrar o reabrir.
/// </summary>
public sealed class ReaperturaVencimientoWorker(IServiceScopeFactory scopeFactory, IOptions<ReaperturaOpciones> opciones, ILogger<ReaperturaVencimientoWorker> logger)
    : BackgroundService
{
    public const string Sistema = "sistema";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (opciones.Value.Deshabilitado)
        {
            logger.LogInformation("ReaperturaVencimientoWorker deshabilitado por configuración.");
            return;
        }
        var intervalo = TimeSpan.FromMinutes(Math.Max(1, opciones.Value.IntervaloMinutos));
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sp = scope.ServiceProvider;
                using var origen = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(ReaperturaVencimientoWorker));
                using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var cerradas = await CerrarVencidasAsync(sp.GetRequiredService<ContabilidadDbContext>(), sp.GetRequiredService<IClock>(),
                    sp.GetRequiredService<ICurrentUserContext>(), sp.GetRequiredService<IAuditCorrelationContext>(), stoppingToken);
                if (cerradas > 0) logger.LogInformation("ReaperturaVencimientoWorker: {Cerradas} periodos cerrados por vencimiento.", cerradas);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "ReaperturaVencimientoWorker: error en la corrida; reintenta en la siguiente.");
            }
            try { await Task.Delay(intervalo, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Cierra los periodos de las reaperturas vencidas a la hora del servidor; devuelve cuántos cerró.</summary>
    public static async Task<int> CerrarVencidasAsync(ContabilidadDbContext db, IClock clock, ICurrentUserContext usuario,
        IAuditCorrelationContext correlacion, CancellationToken ct)
    {
        var ahora = clock.UtcNow;
        var cerradas = 0;
        var vencidas = await db.Reaperturas.IgnoreQueryFilters().AsNoTracking().Where(r => r.Vigente && r.VenceEn <= ahora)
            .Select(r => r.Id).ToListAsync(ct);
        foreach (var id in vencidas)
        {
            db.ChangeTracker.Clear();
            await PostgresAdvisoryLock.ExecuteAsync(db, PoliticaPeriodos.LockPeriodos, async t =>
            {
                var r = await db.Reaperturas.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id && x.Vigente, t);
                if (r is null || !r.Vencida(ahora)) return;
                var p = await db.Periodos.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == r.PeriodoId, t);
                r.Concluir(ahora);
                if (p is { Estado: EstadoPeriodo.Abierto })
                {
                    var transicion = p.CerrarPorVencimiento(ahora, Sistema);
                    AuditoriaPeriodos.Registrar(db, p, transicion, usuario, correlacion);
                    cerradas++;
                }
                await db.SaveChangesAsync(t);
            }, ct);
        }
        return cerradas;
    }
}
