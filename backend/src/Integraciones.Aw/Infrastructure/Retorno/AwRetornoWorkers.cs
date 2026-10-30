using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.Integraciones.Aw.Application.Retorno;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Integraciones.Aw.Infrastructure.Retorno;

/// <summary>
/// TR-01: escucha <c>facturacion.factura-venta.timbrada.v1</c> y <c>facturacion.comprobante.cancelado.v1</c>
/// (topic facturacion-events) y deja la fila Documento (FacturaFinal / Cancelacion) en <c>aw_retorno</c>. La idempotencia vive en la tabla (UUID + tipo, candado por
/// pedido), no en una marca de evento: una reentrega no duplica. Nota de crédito: fuera de TR-01 (el evento
/// no trae folio ni pedido). Mismo patrón que <c>FacturacionEventListenerWorker</c> de CxC.
/// </summary>
public sealed class FacturacionRetornoListenerWorker : BackgroundService, ILocalIntegrationEventConsumer
{
    public const string TopicName = "facturacion-events";
    public const string SubscriptionName = "integraciones-aw-subscription";
    public const string EventTypeFacturaTimbrada = "facturacion.factura-venta.timbrada.v1";
    public const string EventTypeComprobanteCancelado = "facturacion.comprobante.cancelado.v1";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly ServiceBusClient _serviceBusClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FacturacionRetornoListenerWorker> _logger;
    private ServiceBusProcessor? _processor;

    public FacturacionRetornoListenerWorker(
        ServiceBusClient serviceBusClient, IServiceScopeFactory scopeFactory, ILogger<FacturacionRetornoListenerWorker> logger)
    {
        _serviceBusClient = serviceBusClient;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = _serviceBusClient.CreateProcessor(TopicName, SubscriptionName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 1, // el candado por pedido igual serializa; así no se pelean dos conexiones
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5),
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
        });
        _processor.ProcessMessageAsync += args => ProcessAsync(
            args.Message.Subject ?? string.Empty, args.Message.Body.ToString(), args, args.CancellationToken);
        _processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "FacturacionRetornoListener (Aw) error. Source={Source} Entity={Entity}", args.ErrorSource, args.EntityPath);
            return Task.CompletedTask;
        };

        try
        {
            await _processor.StartProcessingAsync(stoppingToken);
            _logger.LogInformation("FacturacionRetornoListener (Aw) iniciado. Topic={Topic} Subscription={Sub}", TopicName, SubscriptionName);
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            if (_processor is { IsProcessing: true }) await _processor.StopProcessingAsync(CancellationToken.None);
            if (_processor is not null) await _processor.DisposeAsync();
            _processor = null;
        }
    }

    string ILocalIntegrationEventConsumer.TopicName => TopicName;

    Task ILocalIntegrationEventConsumer.ConsumirLocalAsync(string eventType, Guid eventId, string body, CancellationToken cancellationToken)
        => ProcessAsync(eventType, body, null, cancellationToken);

    /// <summary>Internal para tests: sin Service Bus relanza la falla (el outbox reintenta); con él abandona para retry.</summary>
    internal async Task ProcessAsync(string eventType, string body, ProcessMessageEventArgs? sb, CancellationToken ct)
    {
        try
        {
            if (eventType is EventTypeFacturaTimbrada or EventTypeComprobanteCancelado)
            {
                using var scope = _scopeFactory.CreateScope();
                var sp = scope.ServiceProvider;
                using var origin = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(FacturacionRetornoListenerWorker));
                using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var servicio = sp.GetRequiredService<AwRetornoDocumentoService>();
                if (eventType == EventTypeFacturaTimbrada)
                    await servicio.RegistrarFacturaAsync(JsonSerializer.Deserialize<FacturaTimbradaRetornoPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null."), ct);
                else
                    await servicio.RegistrarCancelacionAsync(JsonSerializer.Deserialize<CancelacionRetornoPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null."), ct);
            }
            if (sb is not null) await sb.CompleteMessageAsync(sb.Message, ct);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Payload corrupto. EventType={EventType}. Dead-letter.", eventType);
            if (sb is not null) await sb.DeadLetterMessageAsync(sb.Message, "InvalidPayload", ex.Message, ct);
        }
        catch (Exception ex)
        {
            if (sb is null) throw;
            _logger.LogError(ex, "Error procesando evento. EventType={EventType}. Abandono para retry.", eventType);
            await sb.AbandonMessageAsync(sb.Message, cancellationToken: ct);
        }
    }
}

/// <summary>TR-01: cada pasada lee el acuse de <c>aw_retorno</c>; reemplaza las desfasadas y avisa de las detenidas.</summary>
public sealed class AwRetornoAcuseWorker(IServiceScopeFactory scopeFactory, ILogger<AwRetornoAcuseWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ConciliarOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "AwRetornoAcuseWorker: ciclo falló; se reintenta en el siguiente intervalo."); }

            try { await Task.Delay(Intervalo, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task<AwRetornoConciliacion> ConciliarOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        using var origin = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(AwRetornoAcuseWorker));
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await sp.GetRequiredService<AwRetornoDocumentoService>().ConciliarAcusesAsync(ct);
    }
}
