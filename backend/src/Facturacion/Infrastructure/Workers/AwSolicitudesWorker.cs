using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Application.Ingesta.ProcesarSolicitudAw;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Infrastructure.Workers;

/// <summary>
/// Worker en-proceso (D17) que consume la cola <c>aw_solicitud_pedido</c> vía
/// <see cref="IAwSolicitudesReader"/> en orden por (pedido, version) y envía cada
/// solicitud a <see cref="ProcesarSolicitudAwCommand"/> (matriz §12.1). En dev el
/// reader es stub (vacío); con la Hybrid Connection real drena la cola on-prem.
/// </summary>
public sealed class AwSolicitudesWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AwSolicitudesOptions> _options;
    private readonly AwSolicitudesTickSignal _signal;
    private readonly ILogger<AwSolicitudesWorker> _logger;

    public AwSolicitudesWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AwSolicitudesOptions> options,
        AwSolicitudesTickSignal signal,
        ILogger<AwSolicitudesWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _signal = signal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.CurrentValue.Disabled)
        {
            _logger.LogInformation("[AwSolicitudesWorker] Disabled — loop no inicia.");
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
                _logger.LogError(ex, "[AwSolicitudesWorker] Error inesperado.");
            }

            try
            {
                // Espera el intervalo de polling O un nudge (endpoint
                // /integraciones/aw/pedidos/nudge, disparado por la
                // customización A+W tras insertar en la tabla-puente) —
                // lo que llegue primero. El nudge es best-effort: sin él,
                // el polling normal sigue drenando la cola.
                var nudge = await _signal.EsperarAsync(
                    TimeSpan.FromSeconds(_options.CurrentValue.IntervalSeconds), stoppingToken);
                if (nudge)
                    _logger.LogDebug("[AwSolicitudesWorker] Nudge recibido — tick inmediato.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    /// <summary>Internal para tests.</summary>
    internal async Task<int> TickAsync(CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;
        if (opts.EmpresaId == Guid.Empty)
        {
            _logger.LogDebug("[AwSolicitudesWorker] EmpresaId no configurado — no procesa.");
            return 0;
        }

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var reader = sp.GetRequiredService<IAwSolicitudesReader>();
        var sender = sp.GetRequiredService<ISender>();
        var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();

        using var bypass = empresaContext.Bypass();

        var pendientes = (await reader.LeerPendientesAsync(opts.BatchSize, cancellationToken))
            .OrderBy(s => s.NumeroPedido)
            .ThenBy(s => s.Version)
            .ToList();

        var procesadas = 0;
        foreach (var sol in pendientes)
        {
            try
            {
                await sender.Send(new ProcesarSolicitudAwCommand(opts.EmpresaId, sol), cancellationToken);
                procesadas++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AwSolicitudesWorker] Solicitud {Solicitud} ({Pedido}) falló", sol.SolicitudId, sol.NumeroPedido);
            }
        }

        if (pendientes.Count > 0)
            _logger.LogInformation("[AwSolicitudesWorker] Tick: {Total} pendientes, {Procesadas} procesadas.", pendientes.Count, procesadas);

        return procesadas;
    }
}

public sealed class AwSolicitudesOptions
{
    public const string SectionName = "Facturacion:Workers:AwSolicitudes";

    public bool Disabled { get; init; }
    public int IntervalSeconds { get; init; } = 120;
    public int BatchSize { get; init; } = 50;

    /// <summary>Empresa a la que pertenecen los pedidos de la cola A+W (MVP single-tenant). Vacío → no procesa.</summary>
    public Guid EmpresaId { get; init; }

    /// <summary>
    /// Días que una solicitud puede quedarse Pospuesta por causas corregibles
    /// en el ERP (canal/sucursal sin clave_aw, regla G14, cliente no
    /// provisionable) antes de escalar a Rechazada (FAC-ING-PR3). Se mide
    /// desde la primera excepción abierta del pedido en la bandeja.
    /// 0 = rechazo inmediato (comportamiento previo).
    /// </summary>
    public int PospuestaMaxDias { get; init; } = 7;
}
