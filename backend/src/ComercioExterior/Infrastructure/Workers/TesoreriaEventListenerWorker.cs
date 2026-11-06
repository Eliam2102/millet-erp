using System.Text.Json;
using Azure.Messaging.ServiceBus;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.ComercioExterior.Application;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.ComercioExterior.Infrastructure.Workers;

/// <summary>Recibe pagos y depósitos firmes del anticipo; el dominio deduplica por documento bancario.</summary>
public sealed class TesoreriaEventListenerWorker : BackgroundService, ILocalIntegrationEventConsumer
{
    public const string TopicName = "tesoreria-events";
    public const string SubscriptionName = "comercio-exterior-tesoreria-sub";

    // Acepta PascalCase del Outbox y camelCase de productores del mismo contrato.
    // El handler valida las referencias de empresa, expediente y recepción.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ServiceBusClient _serviceBusClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TesoreriaEventListenerWorker> _logger;

    private ServiceBusProcessor? _processor;

    public TesoreriaEventListenerWorker(
        ServiceBusClient serviceBusClient,
        IServiceScopeFactory scopeFactory,
        ILogger<TesoreriaEventListenerWorker> logger)
    {
        _serviceBusClient = serviceBusClient;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 4,
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5),
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
        };

        _processor = _serviceBusClient.CreateProcessor(TopicName, SubscriptionName, options);
        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += OnErrorAsync;

        try
        {
            await _processor.StartProcessingAsync(stoppingToken);
            _logger.LogInformation(
                "FacturacionEventListener iniciado. Topic={Topic} Subscription={Sub}",
                TopicName, SubscriptionName);
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown normal
        }
        finally
        {
            if (_processor is { IsProcessing: true })
            {
                try { await _processor.StopProcessingAsync(CancellationToken.None); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error parando FacturacionEventListener processor.");
                }
            }
            if (_processor is not null)
            {
                await _processor.DisposeAsync();
                _processor = null;
            }
        }
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        var eventType = args.Message.Subject ?? string.Empty;
        var eventId = Guid.TryParse(args.Message.MessageId, out var parsed) ? parsed : Guid.NewGuid();
        var body = args.Message.Body.ToString();

        await ProcessAsync(eventType, eventId, body, args, args.CancellationToken);
    }

    /// <summary>
    /// Internal para tests unit invocar sin levantar SB real.
    /// </summary>
    internal async Task ProcessAsync(
        string eventType,
        Guid eventId,
        string body,
        ProcessMessageEventArgs? smbArgs,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            var mediator = sp.GetRequiredService<IMediator>();
            var originContext = sp.GetRequiredService<IAuditOriginContext>();
            using var origin = originContext.SetOrigin(nameof(TesoreriaEventListenerWorker));
            var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();
            using var bypass = empresaContext.Bypass();

            if (eventType is "tesoreria.pago-factura-proveedor.aplicado.v1" or "tesoreria.anticipo-agente.devolucion-confirmada.v1" or "tesoreria.pago-factura-proveedor.revertido.v1")
            {
                var payload = JsonSerializer.Deserialize<PagoAnticipoCePayload>(body, JsonOpts) ?? throw new JsonException("Payload null.");
                await mediator.Send(new RecibirPagoAnticipoCeCommand(payload, eventType == "tesoreria.anticipo-agente.devolucion-confirmada.v1", eventType == "tesoreria.pago-factura-proveedor.revertido.v1"), cancellationToken);
            }

            if (smbArgs is not null)
            {
                await smbArgs.CompleteMessageAsync(smbArgs.Message, cancellationToken);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "Payload corrupto. EventType={EventType} EventId={EventId}. Dead-letter.",
                eventType, eventId);
            if (smbArgs is not null)
            {
                await smbArgs.DeadLetterMessageAsync(
                    smbArgs.Message, "InvalidPayload", ex.Message, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error procesando evento. EventType={EventType} EventId={EventId}. Abandono para retry.",
                eventType, eventId);
            if (smbArgs is not null)
            {
                await smbArgs.AbandonMessageAsync(smbArgs.Message, cancellationToken: cancellationToken);
            }
            // C1.8: en entrega local (sin smbArgs) no hay mensaje que abandonar;
            // relanzamos para que el outbox reintente la fila.
            if (smbArgs is null) throw;
            // Con Service Bus no re-lanzamos — el SB processor maneja la lifecycle.
        }
    }

    // C1.8 — entrega local sin Service Bus: el eventId es el Id de la fila
    // del outbox. Sin smbArgs, ProcessAsync relanza las fallas para que el
    // outbox reintente.
    string ILocalIntegrationEventConsumer.TopicName => TopicName;

    Task ILocalIntegrationEventConsumer.ConsumirLocalAsync(
        string eventType, Guid eventId, string body, CancellationToken cancellationToken)
        => ProcessAsync(eventType, eventId, body, smbArgs: null, cancellationToken);

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "FacturacionEventListener processor error. Source={Source} Entity={EntityPath}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }
}
