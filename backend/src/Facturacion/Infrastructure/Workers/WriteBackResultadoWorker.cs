using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Infrastructure.Workers;

/// <summary>
/// Worker en-proceso (diseño §9, ADR-0048 D3) que drena los write-backs
/// pendientes de <c>ingesta_control</c> hacia la tabla-puente
/// <c>aw_solicitud_pedido</c> vía <see cref="IAwWriteBackPort"/>:
/// <list type="bullet">
///   <item>uuid + estado_facturacion al timbrar/cancelar (por-pedido,
///   <c>WriteBackSolicitudId = null</c> → el adapter usa <c>Guid.Empty</c>);</item>
///   <item>reintentos del write-back síncrono por-solicitud que falló
///   (HC caída) — la ingesta ya quedó aplicada, solo falta avisar a A+W.</item>
/// </list>
/// Idempotente (UPDATE absoluto en el adapter); respeta <c>MaxIntentos</c>
/// para no martillar un canal caído — el pendiente queda visible en
/// <c>write_back_ultimo_error</c> para diagnóstico.
/// </summary>
public sealed class WriteBackResultadoWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AwWriteBackOptions> _options;
    private readonly ILogger<WriteBackResultadoWorker> _logger;

    public WriteBackResultadoWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AwWriteBackOptions> options,
        ILogger<WriteBackResultadoWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.CurrentValue.Disabled)
        {
            _logger.LogInformation("[WriteBackResultadoWorker] Disabled — loop no inicia.");
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
                _logger.LogError(ex, "[WriteBackResultadoWorker] Error inesperado.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    /// <summary>Internal para tests.</summary>
    internal async Task<int> TickAsync(CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<FacturacionDbContext>();
        var writeBack = sp.GetRequiredService<IAwWriteBackPort>();
        var clock = sp.GetRequiredService<IClock>();
        var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();

        using var bypass = empresaContext.Bypass();

        var pendientes = await db.IngestaControles
            .Where(c => c.WriteBackPendiente && c.WriteBackIntentos < opts.MaxIntentos)
            .OrderBy(c => c.UltimaLecturaAt)
            .Take(opts.BatchSize)
            .ToListAsync(cancellationToken);

        var entregados = 0;
        foreach (var control in pendientes)
        {
            try
            {
                await writeBack.EscribirResultadoAsync(
                    new AwWriteBack(
                        SolicitudId: control.WriteBackSolicitudId ?? Guid.Empty,
                        NumeroPedido: control.ClaveNatural,
                        ErpPedidoId: control.PedidoFacturableId,
                        EstadoFacturacion: control.WriteBackEstado,
                        Uuid: control.WriteBackUuid,
                        Resultado: control.WriteBackResultado ?? ResultadoSolicitudAw.Aplicada,
                        Motivo: control.WriteBackMotivo),
                    cancellationToken);

                control.ConfirmarWriteBack(clock.UtcNow);
                entregados++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                control.RegistrarFalloWriteBack(ex.Message, clock.UtcNow);
                _logger.LogWarning(ex,
                    "[WriteBackResultadoWorker] write-back de {Pedido} falló (intento {Intentos}/{Max}).",
                    control.ClaveNatural, control.WriteBackIntentos, opts.MaxIntentos);
            }
        }

        if (pendientes.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "[WriteBackResultadoWorker] Tick: {Total} pendientes, {Entregados} entregados.",
                pendientes.Count, entregados);
        }

        return entregados;
    }
}

public sealed class AwWriteBackOptions
{
    public const string SectionName = "Facturacion:Workers:AwWriteBack";

    public bool Disabled { get; init; }
    public int IntervalSeconds { get; init; } = 120;
    public int BatchSize { get; init; } = 50;

    /// <summary>
    /// Tope de reintentos por pendiente. Al alcanzarlo, el worker lo deja de
    /// barrer (sigue Pendiente + write_back_ultimo_error para diagnóstico);
    /// subir el valor por config lo re-activa.
    /// </summary>
    public int MaxIntentos { get; init; } = 10;
}
