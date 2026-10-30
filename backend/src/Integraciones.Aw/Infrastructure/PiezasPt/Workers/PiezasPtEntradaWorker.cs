using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.PiezasPt;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.ProductoTerminado.Application.Escaneos;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Infrastructure.PiezasPt;

/// <summary>
/// Lector periódico de escaneos de pieza terminada de A+W (solo lectura; no se escribe acuse en A+W).
/// Apagado por omisión; lote con traslape, no CDC. Un candado consultivo por empresa evita ciclos simultáneos.
/// </summary>
public sealed class PiezasPtEntradaWorker(IServiceScopeFactory scopes, IOptions<PiezasPtOptions> options, ILogger<PiezasPtEntradaWorker> logger) : BackgroundService
{
    private const int MaxPaginasPorCiclo = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ct = stoppingToken;
        if (!Habilitado()) return;
        while (!ct.IsCancellationRequested)
            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.IntervalSeconds), ct); await CorrerUnaVezAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Ciclo de entrada de PT interrumpido; se reintentará sin exponer datos de conexión."); }
    }

    private bool Habilitado() => options.Value.EntradaHabilitada && options.Value.EmpresaId is Guid id && id != Guid.Empty;

    internal static string ClaveCandado(Guid empresaId) => $"millet:pt:piezas:worker:{empresaId}";

    public async Task<int> CorrerUnaVezAsync(CancellationToken ct)
    {
        // Antes de resolver servicios o abrir cualquier conexión, ambos candados deben estar activos.
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

    internal async Task<int> CicloAsync(IServiceProvider sp, CancellationToken ct)
    {
        var o = options.Value; var empresa = o.EmpresaId!.Value;
        EstacionesPtFiltro estaciones;
        try { estaciones = EstacionesPtFiltro.Parse(o.EstacionesPt ?? []); }
        catch (ArgumentException) { logger.LogWarning("Lista de estaciones de PT inválida; ciclo omitido."); return 0; }
        if (estaciones.Vacio) { logger.LogWarning("Lista de estaciones de PT vacía; ciclo omitido."); return 0; }

        // El lector se resuelve una vez por ciclo, dentro del scope del ciclo.
        var reader = sp.GetRequiredService<IPiezasPtOrigenReader>();
        var origen = await reader.OrigenActualAsync(ct);
        var mediator = sp.GetRequiredService<IMediator>();
        await mediator.Send(new ReintentarPospuestosCommand(empresa, o.PospuestoHoras, o.MaxIntentos, o.BatchSize), ct);
        var marca = await mediator.Send(new ObtenerMarcaAguaEscaneosQuery(empresa, origen), ct);
        var desde = (marca ?? new DateTime(2000, 1, 1)).AddMinutes(-o.TraslapeMinutos);

        var enviados = 0; (DateTime Tiempo, string Etiqueta)? cursor = null;
        var fabrica = sp.GetRequiredService<IServiceScopeFactory>();
        for (var pagina = 0; pagina < MaxPaginasPorCiclo; pagina++)
        {
            var filas = await reader.LeerAsync(desde, cursor, estaciones.Rangos, o.BatchSize, ct);
            foreach (var fila in filas)
            {
                cursor = (fila.BearbZeit, fila.Etiqueta);
                if (!fila.Fertig || !estaciones.Contiene(fila.Esnr)) continue;
                try
                {
                    // Un scope (y DbContext) nuevo por fila: el handler limpia el ChangeTracker ante una violación única.
                    using var scopeFila = fabrica.CreateScope();
                    using var contexto = EntrarContexto(scopeFila.ServiceProvider);
                    await scopeFila.ServiceProvider.GetRequiredService<IMediator>().Send(new RegistrarEscaneoPiezaCommand(empresa, origen, fila.Etiqueta,
                        fila.Auftnr, fila.AeIntpos, fila.UPos, fila.TeileNr, fila.Esnr, fila.Area, fila.BearbZeit, fila.ClaveSucursalAw, o.MaxIntentos), ct);
                    enviados++;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception) { logger.LogWarning("No se pudo registrar un escaneo de PT; se continúa con el siguiente."); }
            }
            if (filas.Count < o.BatchSize) break;
        }
        return enviados;
    }

    private Contexto EntrarContexto(IServiceProvider sp) => new Contexto(
        sp.GetRequiredService<ICurrentEmpresaContext>().Bypass(),
        sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(PiezasPtEntradaWorker)),
        sp.GetRequiredService<IAuditCorrelationContext>().Begin(Guid.CreateVersion7()));

    private sealed class Contexto(params IDisposable[] partes) : IDisposable
    {
        public void Dispose() { foreach (var p in partes.Reverse()) p.Dispose(); }
    }
}
