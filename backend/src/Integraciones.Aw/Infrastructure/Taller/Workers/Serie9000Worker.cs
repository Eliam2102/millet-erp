using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Millet.Integraciones.Aw.Infrastructure.PiezasPt;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Infrastructure.Taller.Workers;

/// <summary>
/// Descarga del inventario del taller por pedidos internos Serie 9000 (PT5.10): lee de A+W y baja las hojas de cada
/// posición una sola vez. Solo lectura en A+W; apagado por omisión (<c>Serie9000Habilitada</c>). Incremental por el día
/// del último pedido registrado, con traslape de días; un candado consultivo por empresa evita ciclos simultáneos.
/// Antes de leer reintenta las descargas en revisión.
/// </summary>
public sealed class Serie9000Worker(IServiceScopeFactory scopes, IOptions<PiezasPtOptions> options, ILogger<Serie9000Worker> logger) : BackgroundService
{
    private const int TraslapeDias = 2;
    // ponytail: primera lectura de 7 días hacia atrás; una carga histórica mayor se hace con una fecha de arranque explícita.
    private const int ArranqueDias = 7;
    private const int MaxFilasPorCiclo = 2000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ct = stoppingToken;
        if (!Habilitado()) return;
        while (!ct.IsCancellationRequested)
            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.IntervalSeconds), ct); await CorrerUnaVezAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Ciclo de pedidos Serie 9000 interrumpido; se reintentará sin exponer datos de conexión."); }
    }

    private bool Habilitado() => options.Value.Serie9000Habilitada && options.Value.EmpresaId is Guid id && id != Guid.Empty;

    internal static string ClaveCandado(Guid empresaId) => $"millet:almacen:serie-9000:worker:{empresaId}";

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

    /// <summary>Un ciclo: reintentos de la bandeja y lectura incremental. Devuelve cuántas posiciones nuevas se descargaron.</summary>
    internal async Task<int> CicloAsync(IServiceProvider sp, CancellationToken ct)
    {
        var o = options.Value; var empresa = o.EmpresaId!.Value;
        var reader = sp.GetRequiredService<IPedidosInternosOrigenReader>();
        var origen = await reader.OrigenActualAsync(ct);
        var guardado = sp.GetRequiredService<DescargaTallerGuardado>();

        foreach (var id in await guardado.EnRevisionAsync(empresa, o.BatchSize, ct))
            await EnScopeAsync(g => g.ReintentarAsync(id, ct), "No se pudo reintentar una descarga de Serie 9000 en revisión.", ct);

        var marca = await guardado.MarcaAguaAsync(empresa, origen, ct);
        var desde = (marca ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-ArranqueDias))).AddDays(-TraslapeDias);
        var nuevos = 0;
        foreach (var linea in await reader.LeerAsync(desde, MaxFilasPorCiclo, ct))
            await EnScopeAsync(async g => { if (await g.RegistrarAsync(empresa, origen, linea, ct)) nuevos++; },
                "No se pudo registrar una posición de Serie 9000; se continúa con la siguiente.", ct);
        return nuevos;
    }

    // Un scope (y DbContext) nuevo por posición: un fallo revierte solo esa posición.
    private async Task EnScopeAsync(Func<DescargaTallerGuardado, Task> accion, string aviso, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            using var contexto = EntrarContexto(scope.ServiceProvider);
            await accion(scope.ServiceProvider.GetRequiredService<DescargaTallerGuardado>());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { logger.LogWarning("{Aviso}", aviso); }
    }

    private static Contexto EntrarContexto(IServiceProvider sp) => new(
        sp.GetRequiredService<ICurrentEmpresaContext>().Bypass(),
        sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(Serie9000Worker)),
        sp.GetRequiredService<IAuditCorrelationContext>().Begin(Guid.CreateVersion7()));

    private sealed class Contexto(params IDisposable[] partes) : IDisposable
    {
        public void Dispose() { foreach (var p in partes.Reverse()) p.Dispose(); }
    }
}
