using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Application.Facturas.AplicarPedimento;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Infrastructure.Workers;

/// <summary>
/// Worker en-proceso (§9 diseño) que lee los pedimentos listos
/// (<see cref="ISalidasPedimentosReader"/>, M-216: del expediente de Comercio exterior)
/// y dispara <see cref="AplicarPedimentoCommand"/> para las facturas retenidas en
/// <c>PendientePedimento</c>. La bitácora registra el origen <see cref="OrigenAuditoria"/>.
/// </summary>
public sealed class PedimentoSalidasWorker : BackgroundService
{
    /// <summary>Origen que queda en la bitácora de auditoría al liberar la factura.</summary>
    public const string OrigenAuditoria = "PedimentoExpedienteCe";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<PedimentoSalidasOptions> _options;
    private readonly ILogger<PedimentoSalidasWorker> _logger;

    public PedimentoSalidasWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<PedimentoSalidasOptions> options,
        ILogger<PedimentoSalidasWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.CurrentValue.Disabled)
        {
            _logger.LogInformation("[PedimentoSalidasWorker] Disabled — loop no inicia.");
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
                _logger.LogError(ex, "[PedimentoSalidasWorker] Error inesperado.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    /// <summary>Internal para tests. Devuelve cuántos pedimentos aplicó.</summary>
    internal async Task<int> TickAsync(CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var reader = sp.GetRequiredService<ISalidasPedimentosReader>();
        var sender = sp.GetRequiredService<ISender>();
        var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();

        var originContext = sp.GetRequiredService<IAuditOriginContext>();
        using var origin = originContext.SetOrigin(OrigenAuditoria);
        using var bypass = empresaContext.Bypass();

        var pendientes = await reader.LeerPendientesAsync(opts.BatchSize, cancellationToken);

        var aplicados = 0;
        foreach (var p in pendientes)
        {
            try
            {
                await sender.Send(new AplicarPedimentoCommand(p.FacturaVentaId, p.Pedimento, p.FechaDocAduanero, p.IdentificacionMercancia), cancellationToken);
                aplicados++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[PedimentoSalidasWorker] Pedimento de la factura {Factura} no se aplicó", p.FacturaVentaId);
            }
        }

        if (aplicados > 0)
            _logger.LogInformation("[PedimentoSalidasWorker] Tick: {Aplicados}/{Total} pedimentos aplicados.", aplicados, pendientes.Count);

        return aplicados;
    }
}

public sealed class PedimentoSalidasOptions
{
    public const string SectionName = "Facturacion:Workers:PedimentoSalidas";

    public bool Disabled { get; init; }
    public int IntervalSeconds { get; init; } = 300;
    public int BatchSize { get; init; } = 50;
}
