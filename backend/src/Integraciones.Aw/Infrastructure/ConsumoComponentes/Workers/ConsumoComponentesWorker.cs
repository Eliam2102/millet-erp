using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Millet.Integraciones.Aw.Infrastructure.PiezasPt;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Infrastructure.ConsumoComponentes;

/// <summary>
/// Descuento de componentes por composición (A4.8): lee de A+W las piezas terminadas en ensamblado (argón y
/// separador) y laminación (PVB) y descuenta cada componente una sola vez. Solo lectura en A+W; apagado por omisión
/// (<c>ConsumoComponentesHabilitado</c>). Incremental por marca de agua (último escaneo registrado) con traslape; un
/// candado consultivo por empresa evita ciclos simultáneos. Antes de leer, reintenta los consumos en revisión.
/// </summary>
public sealed class ConsumoComponentesWorker(IServiceScopeFactory scopes, IOptions<PiezasPtOptions> options, ILogger<ConsumoComponentesWorker> logger) : BackgroundService
{
    private const int MaxPaginasPorCiclo = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ct = stoppingToken;
        if (!Habilitado()) return;
        while (!ct.IsCancellationRequested)
            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.IntervalSeconds), ct); await CorrerUnaVezAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Ciclo de consumo de componentes interrumpido; se reintentará sin exponer datos de conexión."); }
    }

    private bool Habilitado() => options.Value.ConsumoComponentesHabilitado && options.Value.EmpresaId is Guid id && id != Guid.Empty;

    internal static string ClaveCandado(Guid empresaId) => $"millet:almacen:consumo-componentes:worker:{empresaId}";

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

    /// <summary>Un ciclo: reintentos de la bandeja y lectura incremental. Devuelve cuántos componentes nuevos se registraron.</summary>
    internal async Task<int> CicloAsync(IServiceProvider sp, CancellationToken ct)
    {
        var o = options.Value; var empresa = o.EmpresaId!.Value;
        var reader = sp.GetRequiredService<IConsumoComponentesOrigenReader>();
        var origen = await reader.OrigenActualAsync(ct);
        var guardado = sp.GetRequiredService<ConsumoComponentesGuardado>();

        // ponytail: reintenta hasta un lote por ciclo, el más antiguo primero; si la bandeja crece mucho, filtrar por causa.
        foreach (var id in await guardado.EnRevisionAsync(empresa, o.BatchSize, ct))
            await EnScopeAsync(g => g.ReintentarAsync(id, ct, options.Value.ConsumoComponentesAfectaInventario), "No se pudo reintentar un consumo en revisión.", ct);

        var marca = await guardado.MarcaAguaAsync(empresa, origen, ct);
        var desde = (marca ?? new DateTime(2000, 1, 1)).AddMinutes(-o.TraslapeMinutos);
        var nuevos = 0; (DateTime Tiempo, string Etiqueta)? cursor = null;
        for (var pagina = 0; pagina < MaxPaginasPorCiclo; pagina++)
        {
            var filas = await reader.LeerAsync(desde, cursor, o.BatchSize, ct);
            foreach (var fila in filas)
                foreach (var pieza in ConsumoComponentesOrigenAdapter.Expandir(fila))
                    await EnScopeAsync(async g => { if (await g.RegistrarAsync(empresa, origen, pieza, ct, options.Value.ConsumoComponentesAfectaInventario)) nuevos++; },
                        "No se pudo registrar el consumo de un componente; se continúa con el siguiente.", ct);
            if (filas.Count < o.BatchSize) break;
            // Cursor inclusivo: el último escaneo se vuelve a leer completo (la llave única evita el doble descuento).
            // ponytail: si un solo escaneo llena la página, el ciclo se corta aquí; subir BatchSize lo resuelve.
            var siguiente = (filas[^1].BearbZeit, filas[^1].Etiqueta);
            if (cursor == siguiente) break;
            cursor = siguiente;
        }
        return nuevos;
    }

    // Un scope (y DbContext) nuevo por componente: un fallo revierte solo ese componente.
    private async Task EnScopeAsync(Func<ConsumoComponentesGuardado, Task> accion, string aviso, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            using var contexto = EntrarContexto(scope.ServiceProvider);
            await accion(scope.ServiceProvider.GetRequiredService<ConsumoComponentesGuardado>());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { logger.LogWarning("{Aviso}", aviso); }
    }

    private static Contexto EntrarContexto(IServiceProvider sp) => new(
        sp.GetRequiredService<ICurrentEmpresaContext>().Bypass(),
        sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(ConsumoComponentesWorker)),
        sp.GetRequiredService<IAuditCorrelationContext>().Begin(Guid.CreateVersion7()));

    private sealed class Contexto(params IDisposable[] partes) : IDisposable
    {
        public void Dispose() { foreach (var p in partes.Reverse()) p.Dispose(); }
    }
}
