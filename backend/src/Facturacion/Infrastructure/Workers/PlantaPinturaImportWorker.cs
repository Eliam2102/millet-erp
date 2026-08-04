using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Application.Ingesta.ImportarPedidoPlantaPintura;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Infrastructure.Workers;

/// <summary>
/// Worker en-proceso (§9 diseño) que hace pull de órdenes facturables de Planta
/// Pintura vía <see cref="IPlantaPinturaPedidosReader"/> y las envía a
/// <see cref="ImportarPedidoPlantaPinturaCommand"/> (exige master, sin
/// auto-provisión). En dev el reader es stub (vacío); con
/// <c>Integraciones.Origenes</c> drena las vistas on-prem.
/// </summary>
public sealed class PlantaPinturaImportWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<PlantaPinturaImportOptions> _options;
    private readonly ILogger<PlantaPinturaImportWorker> _logger;

    public PlantaPinturaImportWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<PlantaPinturaImportOptions> options,
        ILogger<PlantaPinturaImportWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.CurrentValue.Disabled)
        {
            _logger.LogInformation("[PlantaPinturaImportWorker] Disabled — loop no inicia.");
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
                _logger.LogError(ex, "[PlantaPinturaImportWorker] Error inesperado.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    /// <summary>Internal para tests. Devuelve cuántas órdenes procesó.</summary>
    internal async Task<int> TickAsync(CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;
        if (opts.EmpresaId == Guid.Empty)
        {
            _logger.LogDebug("[PlantaPinturaImportWorker] EmpresaId no configurado — no procesa.");
            return 0;
        }

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var reader = sp.GetRequiredService<IPlantaPinturaPedidosReader>();
        var sender = sp.GetRequiredService<ISender>();
        var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();

        using var bypass = empresaContext.Bypass();

        var pendientes = await reader.LeerPendientesAsync(opts.BatchSize, cancellationToken);

        var procesadas = 0;
        foreach (var pedido in pendientes)
        {
            try
            {
                await sender.Send(new ImportarPedidoPlantaPinturaCommand(opts.EmpresaId, pedido), cancellationToken);
                procesadas++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[PlantaPinturaImportWorker] Pedido {Pedido} falló", pedido.NumeroPedido);
            }
        }

        if (pendientes.Count > 0)
            _logger.LogInformation("[PlantaPinturaImportWorker] Tick: {Total} pendientes, {Procesadas} procesadas.", pendientes.Count, procesadas);

        return procesadas;
    }
}

public sealed class PlantaPinturaImportOptions
{
    public const string SectionName = "Facturacion:Workers:PlantaPinturaImport";

    public bool Disabled { get; init; }
    public int IntervalSeconds { get; init; } = 120;
    public int BatchSize { get; init; } = 50;

    /// <summary>Empresa de los pedidos de Planta Pintura (MVP single-tenant). Vacío → no procesa.</summary>
    public Guid EmpresaId { get; init; }
}
