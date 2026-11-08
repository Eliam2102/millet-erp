using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Application.Timbrado.ReintentarTimbrado;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Infrastructure.Workers;

/// <summary>
/// Regla D10 (Manual de Sucursales / Contraste U-1 · 86e3yxn2p):
/// Si la nota de crédito de ranura o de anticipo ligada a una factura timbrada falla al timbrar con el PAC,
/// la factura permanece timbrada y la nota queda en <see cref="EstadoTimbrado.TimbradoFallido"/>.
/// Este worker reintenta automáticamente el timbrado de esas notas de crédito cada 15 minutos (hasta un máximo de 3 intentos).
/// Al alcanzar el tope de 3 intentos, cesan los reintentos automáticos y permanece disponible el reintento manual desde la bandeja.
/// </summary>
public sealed class NotaCreditoPendienteRetryWorker : BackgroundService
{
    private static readonly string[] CodigosAmbiguos =
        ["PAC_TIMEOUT", "PAC_SIN_RESPUESTA", "PAC_RESPUESTA_INCOMPLETA"];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<NotaCreditoPendienteRetryOptions> _options;
    private readonly ILogger<NotaCreditoPendienteRetryWorker> _logger;

    public NotaCreditoPendienteRetryWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<NotaCreditoPendienteRetryOptions> options,
        ILogger<NotaCreditoPendienteRetryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.CurrentValue.Disabled)
        {
            _logger.LogInformation("[NotaCreditoPendienteRetryWorker] Disabled — loop no inicia.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, _options.CurrentValue.IntervalMinutes)), stoppingToken);
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[NotaCreditoPendienteRetryWorker] Error inesperado en el ciclo de reintento.");
            }
        }
    }

    /// <summary>Internal para tests. Devuelve cuántas notas de crédito se reintentaron en esta pasada.</summary>
    internal async Task<int> TickAsync(CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<FacturacionDbContext>();
        var sender = sp.GetRequiredService<ISender>();
        var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();
        var clock = sp.GetRequiredService<IClock>();
        var originContext = sp.GetRequiredService<IAuditOriginContext>();

        using var origin = originContext.SetOrigin(nameof(NotaCreditoPendienteRetryWorker));
        using var bypass = empresaContext.Bypass();

        var ahora = clock.UtcNow;
        var ventanaEspera = TimeSpan.FromMinutes(opts.IntervalMinutes);

        // Candidatas: Notas de crédito ligadas a una factura (ranura, amortización, etc.) en estado TimbradoFallido
        var candidatas = await db.NotasCredito
            .Where(nc => nc.Estado == EstadoTimbrado.TimbradoFallido
                         && nc.FacturaRelacionadaId != null)
            .OrderBy(nc => nc.UpdatedAt)
            .Take(opts.BatchSize)
            .ToListAsync(cancellationToken);

        var reintentadas = 0;

        foreach (var nc in candidatas)
        {
            // Omitir códigos ambiguos que requieren confirmación manual en FiscalAPI
            if (nc.TimbradoErrorCodigo is not null
                && CodigosAmbiguos.Contains(nc.TimbradoErrorCodigo, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            // Consultar cuántos intentos de timbrado se han registrado para este comprobante
            var intentos = await db.BitacorasIntentoTimbrado
                .CountAsync(b => b.ComprobanteId == nc.Id, cancellationToken);

            // Regla D10: hasta 3 intentos automáticos
            if (intentos >= opts.MaxAttempts)
            {
                _logger.LogInformation(
                    "[NotaCreditoPendienteRetryWorker] NC {Id} ({Folio}) alcanzó el tope de {Max} intentos ({Actuales}). Se detiene reintento automático.",
                    nc.Id, nc.Folio, opts.MaxAttempts, intentos);
                continue;
            }

            // Respetar intervalo mínimo de espera (15 minutos) desde el último intento
            if (ahora - nc.UpdatedAt < ventanaEspera && intentos > 0)
            {
                continue;
            }

            _logger.LogInformation(
                "[NotaCreditoPendienteRetryWorker] Reintentando NC {Id} ({Folio}, factura {FacturaId}, intento {Intento}/{Max})...",
                nc.Id, nc.Folio, nc.FacturaRelacionadaId, intentos + 1, opts.MaxAttempts);

            try
            {
                await sender.Send(new ReintentarTimbradoCommand(nc.Id), cancellationToken);
                reintentadas++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[NotaCreditoPendienteRetryWorker] Error al reintentar timbrado de NC {Id} ({Folio}).",
                    nc.Id, nc.Folio);
            }
        }

        return reintentadas;
    }
}

public sealed class NotaCreditoPendienteRetryOptions
{
    public const string SectionName = "Facturacion:Workers:NotaCreditoPendienteRetry";

    public bool Disabled { get; init; }
    public int IntervalMinutes { get; init; } = 15;
    public int MaxAttempts { get; init; } = 3;
    public int BatchSize { get; init; } = 20;
}
