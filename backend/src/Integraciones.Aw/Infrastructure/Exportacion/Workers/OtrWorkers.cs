using MediatR;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Application.Pedidos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Application.Ports;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Infrastructure.Exportacion;
public sealed class OtrOptions
{
    public const string SectionName = "IntegracionesAw:Exportacion";
    public bool EntradaHabilitada { get; set; }
    public bool SalidaHabilitada { get; set; }
    public Guid? EmpresaId { get; set; }
    public int IntervalSeconds { get; set; } = 120;
    public int BatchSize { get; set; } = 50;
    public int MaxIntentos { get; set; } = 5;
    public int AcuseVencidoMinutos { get; set; } = 1440;
    public bool ContratoI134Firmado { get; set; }
}
public abstract class OtrWorker(IServiceScopeFactory scopes, IOptions<OtrOptions> options, ILogger logger, bool entrada) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ct = stoppingToken;
        if (!Habilitado()) return;
        while (!ct.IsCancellationRequested)
            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.IntervalSeconds), ct); await CorrerUnaVezAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Ciclo OTR {Direccion} interrumpido; se reintentará sin exponer datos de conexión.", entrada ? "entrada" : "salida"); }
    }
    private bool Habilitado() => (entrada ? options.Value.EntradaHabilitada : options.Value.SalidaHabilitada) && options.Value.EmpresaId is Guid id && id != Guid.Empty;
    public async Task<int> CorrerUnaVezAsync(CancellationToken ct)
    {
        // Antes de resolver servicios o abrir cualquier conexión, ambos candados deben estar activos.
        if (!Habilitado()) return 0;
        using var scope = scopes.CreateScope(); var sp = scope.ServiceProvider;
        using var empresa = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        using var origen = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(entrada ? nameof(OtrEntradaWorker) : nameof(OtrSalidaWorker));
        using var correlation = sp.GetRequiredService<IAuditCorrelationContext>().Begin(Guid.CreateVersion7());
        var db = sp.GetRequiredService<ComercioExteriorDbContext>();
        var clave = $"millet:ce:otr:worker:{options.Value.EmpresaId}:{entrada}";
        await db.Database.OpenConnectionAsync(ct);
        var adquirido = false;
        try
        {
            adquirido = await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_lock(hashtextextended({0}, 0)) AS \"Value\"", clave).SingleAsync(ct);
            if (!adquirido) return 0;
            return entrada ? await EntradaAsync(sp, db, ct) : await SalidaAsync(sp, db, ct);
        }
        finally
        {
            if (adquirido) await db.Database.SqlQueryRaw<bool>("SELECT pg_advisory_unlock(hashtextextended({0}, 0)) AS \"Value\"", clave).SingleAsync(CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }
    internal async Task<int> EntradaAsync(IServiceProvider sp, ComercioExteriorDbContext db, CancellationToken ct)
    {
        var reader = sp.GetRequiredService<IOtrEntradaReader>(); var cantidad = 0;
        foreach (var fila in await reader.LeerPendientesAsync(options.Value.BatchSize, ct))
        {
            string resultado; string? motivo = null;
            var control = await db.OtrEntradasControl.SingleOrDefaultAsync(x => x.EmpresaId == options.Value.EmpresaId && x.IdOrigen == fila.IdOrigen && x.VersionOrigen == fila.Version, ct);
            if (control is null) { control = new(options.Value.EmpresaId!.Value, fila.IdOrigen, fila.Version); db.OtrEntradasControl.Add(control); }
            if (!control.Rechazada) control.Intentar();
            await db.SaveChangesAsync(ct); db.ChangeTracker.Clear();
            try
            {
                if (control.Rechazada) { resultado = "Rechazada"; motivo = control.UltimoError; }
                else
                {
                    if (fila.ErrorLectura is not null && fila.ErrorTecnico) throw new InvalidOperationException(fila.ErrorLectura);
                    if (fila.ErrorLectura is not null) throw new BusinessRuleException(fila.ErrorLectura, "Revise los datos de la fila OTR.");
                    var contenido = fila.Contenido;
                    if (contenido.ClienteNumeroAw is not null)
                    {
                        var cliente = await sp.GetRequiredService<IClientesReadPort>().ResolverPorReferenciaAsync(contenido.ClienteNumeroAw, ct);
                        contenido = contenido with { ClienteId = cliente is { Activo: true } ? cliente.ClienteId : Guid.Empty };
                    }
                    if (contenido.SucursalClaveAw is not null)
                        contenido = contenido with { SucursalId = await sp.GetRequiredService<ISucursalPorClaveAwResolver>().ResolverAsync(contenido.SucursalClaveAw, ct) ?? Guid.Empty };
                    var aplicada = await sp.GetRequiredService<IMediator>().Send(new AplicarEnvioOtr(options.Value.EmpresaId!.Value, fila with { Contenido = contenido }), ct);
                    resultado = aplicada ? "Aplicada" : "Aplicada (idempotente)"; cantidad++;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                motivo = ex is BusinessRuleException regla ? regla.Code : "CE_OTR_ERROR_TECNICO: " + ex.Message;
                control.Fallar(motivo, ex is BusinessRuleException, options.Value.MaxIntentos);
                motivo = control.UltimoError; resultado = control.Rechazada ? "Rechazada" : "Error";
            }
            finally { db.ChangeTracker.Clear(); }
            db.OtrEntradasControl.Update(control); await db.SaveChangesAsync(ct); db.ChangeTracker.Clear();
            try { await reader.MarcarResultadoAsync(fila.IdOrigen, fila.Version, resultado, motivo, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { logger.LogWarning("No se pudo escribir el resultado de una fila OTR; se recuperará en otro ciclo."); }
        }
        return cantidad;
    }
    private async Task<int> SalidaAsync(IServiceProvider sp, ComercioExteriorDbContext db, CancellationToken ct)
    {
        var writer = sp.GetRequiredService<IOtrSalidaWriter>(); var clock = sp.GetRequiredService<IClock>();
        var filas = await db.OtrSalidas.Where(x => x.EmpresaId == options.Value.EmpresaId && x.Estado == EstadoOtrSalida.Pendiente)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(options.Value.BatchSize).ToListAsync(ct);
        // Esperar acuses antiguos no debe bloquear entregas nuevas ni la consulta de otros acuses.
        filas.AddRange(await db.OtrSalidas.Where(x => x.EmpresaId == options.Value.EmpresaId && x.Estado == EstadoOtrSalida.Entregada)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct));
        IReadOnlyList<OtrAcuse> acuses;
        try { acuses = await writer.LeerAcusesAsync(filas.Select(x => x.Id).ToArray(), ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { acuses = []; }
        foreach (var s in filas)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var acuse = acuses.SingleOrDefault(a => a.SalidaId == s.Id);
            if (acuse is not null) s.Acusar(acuse.Fecha, acuse.Resultado, acuse.Motivo);
            else if (s.Estado == EstadoOtrSalida.Pendiente)
            {
                s.Intentar(options.Value.MaxIntentos, clock.UtcNow);
                try
                {
                    var envio = await db.OtrEnvios.SingleAsync(e => e.Id == s.OtrEnvioId && e.EmpresaId == s.EmpresaId, ct);
                    await writer.EntregarAsync(new(s.Id, envio.IdOrigen, s.Tipo, s.VersionErp, s.ValorJson, s.Intento), ct);
                    s.Entregada();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception) { s.ErrorEntrega(options.Value.MaxIntentos, "CE_OTR_ENTREGA_ERROR: no se pudo entregar a la tabla puente."); }
            }
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        return filas.Count;
    }
}
public sealed class OtrEntradaWorker(IServiceScopeFactory scopes, IOptions<OtrOptions> options, ILogger<OtrEntradaWorker> logger) : OtrWorker(scopes, options, logger, true);
public sealed class OtrSalidaWorker(IServiceScopeFactory scopes, IOptions<OtrOptions> options, ILogger<OtrSalidaWorker> logger) : OtrWorker(scopes, options, logger, false);
