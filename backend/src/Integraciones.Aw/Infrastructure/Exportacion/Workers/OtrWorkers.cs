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
    public Guid? EmpresaId { get; set; }
    public int IntervalSeconds { get; set; } = 120;
    public int BatchSize { get; set; } = 50;
    public int MaxIntentos { get; set; } = 5;
    public bool ContratoI134Firmado { get; set; }
    // Sólo origen Real. El significado de los estatus de ruta lo confirma TI de Millet: por eso son opciones y no reglas fijas.
    /// <summary>Estatus de ruta que no se leen, separados por coma. 50 = en edición (TI).</summary>
    public string EstatusRutaExcluidos { get; set; } = "50";
    /// <summary>Estatus de ruta que cancelan el envío, separados por coma. 890 = Cancelado (Jorge 5-nov; Guadalupe Polanco 6-nov: no se modifica, se cancela y se crea uno nuevo).</summary>
    public string EstatusRutaCancelada { get; set; } = "890";
    /// <summary>Sólo cuentan las posiciones cuyo pedido de A+W tenga STATUS menor a este valor (consulta de TI: &lt; 900).</summary>
    public int EstatusPedidoMenorA { get; set; } = 900;
    /// <summary>Código de unidad de las líneas; debe coincidir con la unidad de la posición del pedido en el ERP.</summary>
    public string UnidadLinea { get; set; } = "H87";
    /// <summary>Si se indica, sólo se leen rutas con fecha de entrega igual o posterior.</summary>
    public DateOnly? EntregaDesde { get; set; }
    internal static bool ListaValida(string? texto) => Lista(texto, out _);
    internal static bool Lista(string? texto, out int[] valores)
    {
        var partes = (texto ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var lista = new int[partes.Length]; valores = lista;
        for (var i = 0; i < partes.Length; i++)
            if (!int.TryParse(partes[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out lista[i])) return false;
        return true;
    }
}
public sealed class OtrEntradaWorker(IServiceScopeFactory scopes, IOptions<OtrOptions> options, ILogger<OtrEntradaWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ct = stoppingToken;
        if (!Habilitado()) return;
        while (!ct.IsCancellationRequested)
            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.IntervalSeconds), ct); await CorrerUnaVezAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Ciclo OTR de entrada interrumpido; se reintentará sin exponer datos de conexión."); }
    }
    private bool Habilitado() => options.Value.EntradaHabilitada && options.Value.EmpresaId is Guid id && id != Guid.Empty;
    public async Task<int> CorrerUnaVezAsync(CancellationToken ct)
    {
        // Antes de resolver servicios o abrir cualquier conexión, ambos candados deben estar activos.
        if (!Habilitado()) return 0;
        using var scope = scopes.CreateScope(); var sp = scope.ServiceProvider;
        using var empresa = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        using var origen = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(OtrEntradaWorker));
        using var correlation = sp.GetRequiredService<IAuditCorrelationContext>().Begin(Guid.CreateVersion7());
        var db = sp.GetRequiredService<ComercioExteriorDbContext>();
        var clave = $"millet:ce:otr:worker:{options.Value.EmpresaId}:entrada";
        await db.Database.OpenConnectionAsync(ct);
        var adquirido = false;
        try
        {
            adquirido = await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_lock(hashtextextended({0}, 0)) AS \"Value\"", clave).SingleAsync(ct);
            if (!adquirido) return 0;
            return await EntradaAsync(sp, db, ct);
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
}
