using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Infrastructure.Workers;

/// <summary>
/// Worker en-proceso (F12-PR2, diseño 04-cuidados-infra §3) que resuelve los
/// comprobantes atascados en <see cref="EstadoTimbrado.TimbradoEnProceso"/> —
/// el estado AMBIGUO que deja el adapter real cuando hay timeout/error de red
/// después de enviar la solicitud al PAC (el camino síncrono normal nunca lo
/// produce).
///
/// <para>
/// Semántica conservadora: pasado el umbral, el comprobante se marca
/// <c>TimbradoFallido("PAC_TIMEOUT")</c> — corregible, vuelve a Borrador para
/// re-emitir. <b>NUNCA re-timbra automáticamente</b>: si la solicitud original
/// SÍ llegó al SAT, re-emitir duplicaría el CFDI. El runbook (08-operacion §F12)
/// instruye verificar en el dashboard de FiscalAPI si existe un timbre con la
/// misma serie+folio antes de re-emitir; si existe, se adopta manualmente.
/// La reconciliación automática por serie+folio queda como
/// PLATFORM-TODO(&lt;TimbradoReconciliacionFolio&gt;) — el SDK no expone
/// búsqueda por folio en la superficie actual.
/// </para>
/// </summary>
public sealed class TimbradoPendienteWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<TimbradoPendienteOptions> _options;
    private readonly ILogger<TimbradoPendienteWorker> _logger;

    public TimbradoPendienteWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<TimbradoPendienteOptions> options,
        ILogger<TimbradoPendienteWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.CurrentValue.Disabled)
        {
            _logger.LogInformation("[TimbradoPendienteWorker] Disabled — loop no inicia.");
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
                _logger.LogError(ex, "[TimbradoPendienteWorker] Error inesperado.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    /// <summary>Internal para tests. Devuelve cuántos comprobantes marcó como fallidos.</summary>
    internal async Task<int> TickAsync(CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<FacturacionDbContext>();
        var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();
        var clock = sp.GetRequiredService<IClock>();

        using var bypass = empresaContext.Bypass();

        var corte = clock.UtcNow.AddMinutes(-opts.UmbralMinutos);
        var atascados = await db.Comprobantes
            .Where(c => c.Estado == EstadoTimbrado.TimbradoEnProceso && c.UpdatedAt < corte)
            .OrderBy(c => c.UpdatedAt)
            .Take(opts.BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var comprobante in atascados)
        {
            comprobante.MarcarTimbradoFallido(
                "PAC_TIMEOUT",
                $"Sin confirmación del PAC después de {opts.UmbralMinutos} min. ANTES de re-emitir, " +
                $"verificar en FiscalAPI si existe un timbre con el folio {comprobante.Folio} (runbook F12).");
            _logger.LogWarning(
                "[TimbradoPendienteWorker] Comprobante {Id} ({Folio}) atascado en TimbradoEnProceso desde {Desde} → TimbradoFallido(PAC_TIMEOUT).",
                comprobante.Id, comprobante.Folio, comprobante.UpdatedAt);
        }

        if (atascados.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        return atascados.Count;
    }
}

public sealed class TimbradoPendienteOptions
{
    public const string SectionName = "Facturacion:Workers:TimbradoPendiente";

    public bool Disabled { get; init; }
    public int IntervalSeconds { get; init; } = 300;

    /// <summary>Minutos en TimbradoEnProceso antes de marcar PAC_TIMEOUT.</summary>
    public int UmbralMinutos { get; init; } = 30;

    public int BatchSize { get; init; } = 20;
}
