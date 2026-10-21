using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Infrastructure.Workers;

/// <summary>
/// Revisión diaria de incidencias del proveedor con compromiso vencido (F1-COM-07, C3.5-b/d).
/// Por cada incidencia abierta cuyo compromiso vigente ya pasó, registra un aviso al responsable
/// en <c>compras.oc_incidencias_avisos</c>, único por (incidencia, fecha compromiso): correr dos
/// veces no repite el aviso; una fecha reprogramada que vuelve a vencer genera otro. No cambia la
/// incidencia ni la OC.
/// </summary>
public sealed class IncidenciaVencidaWorker(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<IncidenciaVencidaOptions> options,
    ILogger<IncidenciaVencidaWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.CurrentValue.Disabled)
        {
            logger.LogInformation("[IncidenciaVencidaWorker] Disabled — loop no inicia.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "[IncidenciaVencidaWorker] Error inesperado.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.CurrentValue.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    /// <summary>Una revisión. Pública para las pruebas de integración; devuelve cuántos avisos registró.</summary>
    public async Task<int> TickAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ComprasDbContext>();
        var clock = sp.GetRequiredService<IClock>();
        using var origin = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(IncidenciaVencidaWorker));
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();

        var ahora = clock.UtcNow;
        var hoy = FechaContable.HoyLocal(ahora);
        var vencidas = await (
            from i in db.IncidenciasProveedorOc.AsNoTracking()
            join oc in db.OrdenesCompra.AsNoTracking() on i.OrdenCompraId equals oc.Id
            where i.CerradaEn == null && oc.Estado != EstadoOrdenCompra.Cancelada
            let vigente = i.Compromisos.OrderByDescending(c => c.Numero).Select(c => c.FechaCompromiso).First()
            where vigente < hoy && !db.AvisosIncidenciaProveedorOc.Any(
                a => a.IncidenciaProveedorOcId == i.Id && a.FechaCompromiso == vigente)
            select new { i.Id, i.OrdenCompraId, i.Numero, i.ResponsableId, Folio = oc.Folio, Vigente = vigente })
            .ToListAsync(cancellationToken);

        var enviados = 0;
        foreach (var v in vencidas)
        {
            db.AvisosIncidenciaProveedorOc.Add(
                new AvisoIncidenciaProveedorOc(v.Id, v.OrdenCompraId, v.Vigente, v.ResponsableId, ahora));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
            {
                // Otra instancia ya registró ese aviso.
                db.ChangeTracker.Clear();
                continue;
            }

            // PLATFORM-TODO(<Notificaciones>): enviar por correo/SignalR al responsable cuando
            // exista el módulo de notificaciones (ADR-0026). Hoy el aviso queda registrado y visible en la OC.
            logger.LogWarning(
                "[IncidenciaVencidaWorker] Compromiso vencido: OC {Folio} incidencia {Numero} " +
                "(fecha {Fecha}) — aviso al responsable {Responsable}.",
                v.Folio.Valor, v.Numero, v.Vigente, v.ResponsableId);
            enviados++;
        }

        logger.LogInformation("[IncidenciaVencidaWorker] Tick: {Vencidas} vencidas sin aviso, {Enviados} avisos.",
            vencidas.Count, enviados);
        return enviados;
    }
}

public sealed class IncidenciaVencidaOptions
{
    public const string SectionName = "Compras:Workers:IncidenciasVencidas";

    public bool Disabled { get; init; }

    /// <summary>Una revisión al arrancar y luego cada intervalo (por omisión diario).</summary>
    public int IntervalSeconds { get; init; } = 24 * 60 * 60;
}
