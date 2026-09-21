using System.Text.Json;
using Azure.Messaging.ServiceBus;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorCobrar.Application.EventListeners;
using Millet.CuentasPorCobrar.Domain.Eventos;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.Infrastructure.Workers;

/// <summary>
/// <c>BackgroundService</c> que se conecta al topic <c>facturacion-events</c>
/// (publicado por <c>OutboxPublisherWorker&lt;FacturacionDbContext&gt;</c>) y
/// alimenta la proyección <c>factura_cartera</c> (CXC-PR3, levantamiento §0):
/// alta (factura-venta timbrada), pagos (REPP + cobro mostrador ± cancelado),
/// NC, reversa (comprobante cancelado) y anticipos (informativo).
///
/// <para>
/// Mismo patrón que <c>CuentasPorPagar.Infrastructure.Workers.AlmacenEventListenerWorker</c>:
/// dedupe en <see cref="EventoProcesado"/> antes de despachar; payloads
/// corruptos van a dead-letter; errores transitorios (incluida la factura
/// aún no proyectada por entrega fuera de orden) se abandonan para retry.
/// La subscription <c>cuentas-por-cobrar-subscription</c> se crea vía Bicep
/// con filtro SQL por <c>EventType IN (...)</c>.
/// </para>
///
/// <para>
/// <b>Empresa bypass</b>: el worker corre fuera de cualquier request
/// HTTP — levanta <see cref="ICurrentEmpresaContext.Bypass"/> para que el
/// query filter multi-tenant no excluya las inserciones.
/// </para>
/// </summary>
public sealed class FacturacionEventListenerWorker : BackgroundService
{
    public const string TopicName = "facturacion-events";
    public const string SubscriptionName = "cuentas-por-cobrar-subscription";

    // PropertyNameCaseInsensitive: el OutboxSaveChangesInterceptor serializa
    // con PascalCase (mismo bug detectado en PR #293 — sin esto los Guid
    // llegan como Guid.Empty).
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ServiceBusClient _serviceBusClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FacturacionEventListenerWorker> _logger;

    private ServiceBusProcessor? _processor;

    public FacturacionEventListenerWorker(
        ServiceBusClient serviceBusClient,
        IServiceScopeFactory scopeFactory,
        ILogger<FacturacionEventListenerWorker> logger)
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
                "FacturacionEventListener (CxC) iniciado. Topic={Topic} Subscription={Sub}",
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
            var db = sp.GetRequiredService<CuentasPorCobrarDbContext>();
            var mediator = sp.GetRequiredService<IMediator>();
            var originContext = sp.GetRequiredService<IAuditOriginContext>();
            using var origin = originContext.SetOrigin(nameof(FacturacionEventListenerWorker));
            var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();
            using var bypass = empresaContext.Bypass();

            var yaProcesado = await db.Set<EventoProcesado>()
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
                case FacturaVentaTimbradaHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<FacturaVentaTimbradaPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new FacturaVentaTimbradaCommand(eventId, payload), cancellationToken);
                    break;
                }

                case ReciboPagoTimbradoHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<ReciboPagoTimbradoPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new ReciboPagoTimbradoCommand(eventId, payload), cancellationToken);
                    break;
                }

                case CobroMostradorRegistradoHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<CobroMostradorRegistradoPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new CobroMostradorRegistradoCommand(eventId, payload), cancellationToken);
                    break;
                }

                case CobroMostradorCanceladoHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<CobroMostradorCanceladoPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new CobroMostradorCanceladoCommand(eventId, payload), cancellationToken);
                    break;
                }

                case NotaCreditoTimbradaHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<NotaCreditoTimbradaPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new NotaCreditoTimbradaCommand(eventId, payload), cancellationToken);
                    break;
                }

                case ComprobanteCanceladoHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<ComprobanteCanceladoPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new ComprobanteCanceladoCommand(eventId, payload), cancellationToken);
                    break;
                }

                case FacturaAnticipoTimbradaHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<FacturaAnticipoTimbradaPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new FacturaAnticipoTimbradaCommand(eventId, payload), cancellationToken);
                    break;
                }

                default:
                {
                    _logger.LogWarning(
                        "EventType no manejado por FacturacionEventListener (CxC): {EventType}. Dead-letter.", eventType);
                    if (smbArgs is not null)
                    {
                        await smbArgs.DeadLetterMessageAsync(
                            smbArgs.Message, "UnknownEventType",
                            $"CxC no consume '{eventType}'.", cancellationToken);
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
            "FacturacionEventListener (CxC) processor error. Source={Source} Entity={EntityPath}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }
}
