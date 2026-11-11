using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.PiezasPt;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.ProductoTerminado.Application.Salidas;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Infrastructure.PiezasPt.Workers;

/// <summary>
/// Descuento de PT por albaranes de A+W (el albarán vive en A+W, decisión 10-nov). Lee <c>vw_erp_albaran_pos</c> y, por cada
/// posición, manda a PT todos sus albaranes; PT es idempotente por pedido + posición + albarán. Solo lectura en A+W; apagado
/// por omisión (<c>AlbaranAwHabilitado</c>). Un candado consultivo por empresa evita ciclos simultáneos.
/// ponytail: sin cursor; relee una ventana de días (la idempotencia de PT evita duplicar). Cursor si el volumen lo pide.
/// </summary>
public sealed class AlbaranesAwWorker(IServiceScopeFactory scopes, IOptions<PiezasPtOptions> options, ILogger<AlbaranesAwWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ct = stoppingToken;
        if (!Habilitado()) return;
        while (!ct.IsCancellationRequested)
            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.IntervalSeconds), ct); await CorrerUnaVezAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Ciclo de albaranes de A+W interrumpido; se reintentará sin exponer datos de conexión."); }
    }

    private bool Habilitado() => options.Value.AlbaranAwHabilitado && options.Value.EmpresaId is Guid id && id != Guid.Empty;

    internal static string ClaveCandado(Guid empresaId) => $"millet:pt:albaran-aw:worker:{empresaId}";

    public async Task<int> CorrerUnaVezAsync(CancellationToken ct)
    {
        if (!Habilitado()) return 0;
        var empresaId = options.Value.EmpresaId!.Value;
        using var scope = scopes.CreateScope(); var sp = scope.ServiceProvider;
        using var contexto = EntrarContexto(sp);
        var db = sp.GetRequiredService<IntegracionesAwDbContext>();
        var clave = ClaveCandado(empresaId);
        await db.Database.OpenConnectionAsync(ct);
        var adquirido = false;
        try
        {
            adquirido = await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_lock(hashtextextended({0}, 0)) AS \"Value\"", clave).SingleAsync(ct);
            if (!adquirido) return 0;
            return await CicloAsync(sp, ct);
        }
        finally
        {
            if (adquirido) await db.Database.SqlQueryRaw<bool>("SELECT pg_advisory_unlock(hashtextextended({0}, 0)) AS \"Value\"", clave).SingleAsync(CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Un ciclo: lee la ventana y manda una orden por posición. Devuelve cuántas piezas se descontaron.</summary>
    internal async Task<int> CicloAsync(IServiceProvider sp, CancellationToken ct)
    {
        var o = options.Value; var empresa = o.EmpresaId!.Value;
        var reader = sp.GetRequiredService<IAlbaranesAwOrigenReader>();
        var desde = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-o.AlbaranAwVentanaDias));
        IReadOnlyList<AlbaranPosicionOrigen> filas;
        try { filas = await reader.LeerAsync(desde, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Una falla de lectura (red, vista, permisos) se reintenta en el siguiente intervalo; sin datos de conexión en el log.
            logger.LogWarning("No se pudo leer vw_erp_albaran_pos; se reintentará en el siguiente intervalo.");
            return 0;
        }
        var descontadas = 0;
        foreach (var orden in Agrupar(empresa, filas, logger))
        {
            try
            {
                // Un scope (y DbContext) nuevo por posición: un fallo revierte solo esa posición.
                using var scopeFila = scopes.CreateScope();
                using var contexto = EntrarContexto(scopeFila.ServiceProvider);
                descontadas += await scopeFila.ServiceProvider.GetRequiredService<IMediator>().Send(orden, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { logger.LogWarning("No se pudo aplicar un albarán de A+W a PT; se continúa con la siguiente posición."); }
        }
        return descontadas;
    }

    /// <summary>
    /// Una orden por pedido + posición con todos sus albaranes (pedido que lleva el albarán + folio). Piezas no enteras se
    /// truncan con aviso (las posiciones de servicio no tienen piezas en PT).
    /// </summary>
    internal static IEnumerable<AplicarAlbaranesAwCommand> Agrupar(Guid empresa, IEnumerable<AlbaranPosicionOrigen> filas, ILogger? logger = null) =>
        filas.Where(f => !string.IsNullOrWhiteSpace(f.NumeroPedido))
            .GroupBy(f => (Pedido: f.NumeroPedido.Trim(), f.Posicion))
            .Select(g => new AplicarAlbaranesAwCommand(empresa, g.Key.Pedido, g.Key.Posicion,
                g.GroupBy(f => (PedidoAlbaran: f.PedidoAlbaran.Trim(), f.Folio))
                    .Select(a => new AlbaranAwPiezas(a.Key.PedidoAlbaran, a.Key.Folio, a.Min(f => f.Fecha), Piezas(g.Key.Pedido, g.Key.Posicion, a.Key.Folio, a.Sum(f => Math.Max(0, f.Piezas)), logger)))
                    .ToList()));

    private static int Piezas(string pedido, int posicion, int folio, decimal piezas, ILogger? logger)
    {
        var enteras = (int)decimal.Truncate(piezas);
        if (enteras != piezas)
            logger?.LogWarning("Albarán {Folio} de A+W, pedido {Pedido} posición {Posicion}: {Piezas} piezas no enteras; se descuentan {Enteras}.",
                folio, pedido, posicion, piezas, enteras);
        return enteras;
    }

    private static Contexto EntrarContexto(IServiceProvider sp) => new(
        sp.GetRequiredService<ICurrentEmpresaContext>().Bypass(),
        sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(AlbaranesAwWorker)),
        sp.GetRequiredService<IAuditCorrelationContext>().Begin(Guid.CreateVersion7()));

    private sealed class Contexto(params IDisposable[] partes) : IDisposable
    {
        public void Dispose() { foreach (var p in partes.Reverse()) p.Dispose(); }
    }
}
