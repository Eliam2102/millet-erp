using System.Text.Json;
using Azure.Messaging.ServiceBus;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.Facturacion.Application.EventListeners;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Infrastructure.Workers;

/// <summary>
/// <c>BackgroundService</c> que se conecta al topic <c>tesoreria-events</c>
/// (PR gemelo de TES-PR7): cada <c>pago-cliente.confirmado.v1</c> invoca
/// <c>EmitirReppCommand</c> — la automatización que cierra
/// PLATFORM-TODO(&lt;PagoClienteConfirmado&gt;). CxC aplica a cartera al
/// consumir el <c>recibo-pago.timbrado.v1</c> resultante, y Tesorería lo
/// consume para marcar la confirmación como fiscalmente cubierta.
///
/// <para>
/// Mismo patrón que los listeners de la triada: dedupe en
/// <c>facturacion.evento_procesado</c> antes de despachar; payloads
/// corruptos a dead-letter; errores transitorios se abandonan para retry
/// (<c>MaxDeliveryCount=5</c>). Los errores de REGLA DE NEGOCIO del REPP
/// (sin saldo, no PPD, período cerrado) no llegan aquí: el handler los
/// marca procesados y deja el caso al endpoint manual.
/// </para>
/// </summary>
public sealed class TesoreriaEventListenerWorker : BackgroundService
{
    public const string TopicName = "tesoreria-events";
    public const string SubscriptionName = "facturacion-tesoreria-sub";

    // PropertyNameCaseInsensitive: el OutboxSaveChangesInterceptor serializa
    // con PascalCase (mismo bug detectado en PR #293).
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
            MaxConcurrentCalls = 1, // la reserva de folio es secuencial por serie
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
                "TesoreriaEventListener (Facturación) iniciado. Topic={Topic} Subscription={Sub}",
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
                    _logger.LogWarning(ex, "Error parando TesoreriaEventListener processor.");
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
    /// Internal para tests unitarios — invoca sin levantar SB real.
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
            var db = sp.GetRequiredService<FacturacionDbContext>();
            var mediator = sp.GetRequiredService<IMediator>();
            var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();
            using var bypass = empresaContext.Bypass();

            var yaProcesado = await db.EventosProcesados
                .AsNoTracking()
                .AnyAsync(e => e.EventoId == eventId && e.EventoTipo == eventType, cancellationToken);
            if (yaProcesado)
            {
                _logger.LogDebug(
                    "Evento ya procesado (dedupe). EventType={EventType} EventId={EventId}",
                    eventType, eventId);
                if (smbArgs is not null) await smbArgs.CompleteMessageAsync(smbArgs.Message, cancellationToken);
                return;
            }

            switch (eventType)
            {
                case PagoClienteConfirmadoPayload.EventType:
                {
                    var payload = JsonSerializer.Deserialize<PagoClienteConfirmadoPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new EmitirReppDesdePagoConfirmadoCommand(eventId, payload), cancellationToken);
                    break;
                }

                default:
                {
                    _logger.LogWarning(
                        "EventType no manejado por TesoreriaEventListener (Facturación): {EventType}. Dead-letter.",
                        eventType);
                    if (smbArgs is not null)
                    {
                        await smbArgs.DeadLetterMessageAsync(
                            smbArgs.Message, "UnknownEventType",
                            $"Facturación no consume '{eventType}'.", cancellationToken);
                    }
                    return;
                }
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
        }
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "TesoreriaEventListener (Facturación) processor error. Source={Source} Entity={EntityPath}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }
}
