using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.Events;
using Millet.CuentasPorPagar.Domain.Notificaciones;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Infrastructure.Workers;

/// <summary>
/// Worker en-proceso (ADR-0022, A19 del 01-diseno) que cada
/// <see cref="NotaCreditoEnEsperaOptions.IntervalSeconds"/> intenta
/// hacer match de NCs <see cref="EstadoNotaCredito.EnEspera"/> contra
/// facturas nuevas del proveedor por UUID de relación CFDI.
///
/// <para>
/// **Política A19**: si una NC pasa 30 días en EnEspera sin match, el
/// worker la deja en estado actual + emite log de alerta (PLATFORM-TODO
/// con <c>INotificacionService</c> cuando el módulo real exista). No se
/// cancela automáticamente — el Auxiliar decide.
/// </para>
/// </summary>
public sealed class NotaCreditoEnEsperaMatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<NotaCreditoEnEsperaOptions> _options;
    private readonly ILogger<NotaCreditoEnEsperaMatchWorker> _logger;

    public NotaCreditoEnEsperaMatchWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<NotaCreditoEnEsperaOptions> options,
        ILogger<NotaCreditoEnEsperaMatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.CurrentValue;
        if (opts.Disabled)
        {
            _logger.LogInformation("[NotaCreditoEnEsperaMatchWorker] Disabled — loop no inicia.");
            return;
        }

        _logger.LogInformation(
            "[NotaCreditoEnEsperaMatchWorker] Iniciado. Interval={Interval}s, AlertaDespuesDeDias={AlertaDias}.",
            opts.IntervalSeconds, opts.AlertaDespuesDeDias);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[NotaCreditoEnEsperaMatchWorker] Error inesperado.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    /// <summary>Internal para tests.</summary>
    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<CuentasPorPagarDbContext>();
        var mediator = sp.GetRequiredService<IMediator>();
        var clock = sp.GetRequiredService<IClock>();
        var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();

        var originContext = sp.GetRequiredService<IAuditOriginContext>();
        using var origin = originContext.SetOrigin(nameof(NotaCreditoEnEsperaMatchWorker));
        using var bypass = empresaContext.Bypass();

        var opts = _options.CurrentValue;
        var ahora = clock.UtcNow;
        var umbralAlerta = ahora.AddDays(-opts.AlertaDespuesDeDias);

        var enEspera = await db.NotasCreditoProveedor
            .Where(n => n.Estado == EstadoNotaCredito.EnEspera)
            .ToListAsync(cancellationToken);

        if (enEspera.Count == 0)
        {
            _logger.LogDebug("[NotaCreditoEnEsperaMatchWorker] Nada que matchear — 0 NCs EnEspera.");
            return;
        }

        var vinculadas = 0;
        var alertadas = 0;

        foreach (var nc in enEspera)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Match contra factura por UUID + proveedor.
            var matchFactura = await db.FacturasProveedor
                .AsNoTracking()
                .Where(f => f.UuidCfdi == nc.UuidRelacionCfdi && f.ProveedorId == nc.ProveedorId)
                .Select(f => new { f.Id })
                .FirstOrDefaultAsync(cancellationToken);

            if (matchFactura is not null)
            {
                nc.VincularFacturaOrigen(matchFactura.Id, ahora);
                vinculadas++;

                await mediator.Publish(new NotaCreditoProveedorRegistradaDomainEvent(
                    EmpresaId: nc.EmpresaId,
                    NotaCreditoId: nc.Id,
                    ProveedorId: nc.ProveedorId,
                    FacturaOrigenId: nc.FacturaOrigenId,
                    TipoRelacionCfdi: (int)nc.TipoRelacionCfdi,
                    Total: nc.Total,
                    OcurridoEn: ahora), cancellationToken);

                _logger.LogInformation(
                    "[NotaCreditoEnEsperaMatchWorker] NC {Nc} vinculada a factura {Factura} (UUID {Uuid}).",
                    nc.Id, matchFactura.Id, nc.UuidRelacionCfdi);
                continue;
            }

            // Sin match: si lleva >umbral días en EnEspera, alerta.
            if (nc.FechaCaptura <= umbralAlerta)
            {
                _logger.LogWarning(
                    "[NotaCreditoEnEsperaMatchWorker] NC {Nc} lleva {Dias} días en EnEspera sin match (UUID {Uuid}). Alertando al Auxiliar (PLATFORM-TODO).",
                    nc.Id, (int)(ahora - nc.FechaCaptura).TotalDays, nc.UuidRelacionCfdi);
                alertadas++;
            }
        }

        if (vinculadas > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "[NotaCreditoEnEsperaMatchWorker] Tick: {Total} en espera, {Vinculadas} vinculadas, {Alertadas} alertadas por antigüedad.",
            enEspera.Count, vinculadas, alertadas);
    }
}

public sealed class NotaCreditoEnEsperaOptions
{
    public const string SectionName = "CuentasPorPagar:Workers:NotaCreditoEnEspera";

    public bool Disabled { get; init; }

    /// <summary>Default diario (24h).</summary>
    public int IntervalSeconds { get; init; } = 24 * 60 * 60;

    /// <summary>Días sin match para emitir alerta (A19: 30 días).</summary>
    public int AlertaDespuesDeDias { get; init; } = 30;
}
