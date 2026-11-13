using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Facturacion.Infrastructure.Workers;

/// <summary>
/// Worker en-proceso (diseño §9, ADR-0048 D3) que drena los write-backs
/// pendientes de <c>ingesta_control</c> hacia la tabla-puente
/// <c>aw_solicitud_pedido</c> vía <see cref="IAwWriteBackPort"/>:
/// <list type="bullet">
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

        var originContext = sp.GetRequiredService<IAuditOriginContext>();
        using var origin = originContext.SetOrigin(nameof(WriteBackResultadoWorker));
        using var bypass = empresaContext.Bypass();

        var candidatos = await db.IngestaControles
            // TR-01: solo reintentos por-solicitud; el write-back por-pedido (B6) quedó apagado.
            .Where(c => c.WriteBackPendiente && c.WriteBackSolicitudId != null && c.WriteBackIntentos < opts.MaxIntentos)
            .OrderBy(c => c.UltimaLecturaAt)
            .Take(opts.BatchSize * 20)
            .ToListAsync(cancellationToken);
        // I13.6 · espera creciente: tras la falla n se espera intervalo × 2^(n−1), hasta el tope.
        // UltimaLecturaAt guarda la hora de la última falla (RegistrarFalloWriteBack).
        var ahora = clock.UtcNow;
        var pendientes = candidatos
            .Where(c => c.UltimaLecturaAt + EsperaAntesDe(c.WriteBackIntentos, opts) <= ahora)
            .Take(opts.BatchSize)
            .ToList();

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

    /// <summary>Espera mínima desde la última falla antes de volver a intentar (I13.6).</summary>
    internal static TimeSpan EsperaAntesDe(int fallas, AwWriteBackOptions opts) =>
        EsperaReintento.Calcular(fallas, TimeSpan.FromSeconds(opts.IntervalSeconds), TimeSpan.FromSeconds(opts.BackoffMaxSeconds));
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

    /// <summary>
    /// I13.6 · espera máxima entre dos intentos del mismo pendiente. La espera empieza en
    /// <see cref="IntervalSeconds"/> y se duplica en cada falla hasta este tope: con los
    /// valores por omisión (2 min, 30 min, 10 intentos) un pendiente se sigue intentando
    /// unas 3 horas, así que una caída de A+W de una hora se recupera sola (CA13.6).
    /// </summary>
    public int BackoffMaxSeconds { get; init; } = 1800;
}
