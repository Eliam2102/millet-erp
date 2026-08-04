using System.Text.Json;
using Azure.Messaging.ServiceBus;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Application.EventListeners;
using Millet.CuentasPorPagar.Application.EventListeners.Tesoreria;
using Millet.CuentasPorPagar.Domain.Eventos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Infrastructure.Workers;

/// <summary>
/// <c>BackgroundService</c> que se conecta al topic <c>tesoreria-events</c>
/// y procesa los 4 eventos de Tesorería que CxP suscribe (F9-PR1):
/// <list type="bullet">
///   <item><c>tesoreria.pago-factura-proveedor.aplicado.v1</c></item>
///   <item><c>tesoreria.pago-factura-proveedor.revertido.v1</c></item>
///   <item><c>tesoreria.repp-proveedor.recibido.v1</c></item>
///   <item><c>tesoreria.cancelacion-pasivo.solicitada.v1</c></item>
/// </list>
///
/// <para>
/// Mismo patrón que <c>AlmacenEventListenerWorker</c>: dedupe en
/// <c>EventoProcesado</c>, dead-letter para EventType desconocido y
/// JsonException, abandono para retry en errores transitorios.
/// </para>
///
/// <para>
/// La subscription <c>cuentas-por-pagar-tesoreria-sub</c> en el topic
/// <c>tesoreria-events</c> se crea via Bicep/portal. Tesorería puede
/// no existir todavía como módulo interno (puede ser un sistema externo)
/// — el worker está listo para consumir desde el momento en que el topic
/// reciba mensajes.
/// </para>
/// </summary>
public sealed class TesoreriaEventListenerWorker : BackgroundService
{
    public const string TopicName = "tesoreria-events";
    public const string SubscriptionName = "cuentas-por-pagar-tesoreria-sub";

    // PropertyNameCaseInsensitive = true: Tesorería puede no existir
    // todavía como módulo interno (puede ser un sistema externo) y no
    // tenemos contrato firmado sobre la convención de naming del wire.
    // Blindamos el consumer para aceptar tanto PascalCase como camelCase
    // — cualquier convención que Tesorería adopte cuando exista, este
    // worker la deserializa. Mismo patrón defensivo que #299.
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
                "TesoreriaEventListener iniciado. Topic={Topic} Subscription={Sub}",
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
            var db = sp.GetRequiredService<CuentasPorPagarDbContext>();
            var mediator = sp.GetRequiredService<IMediator>();
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
                case PagoFacturaProveedorAplicadoHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<PagoFacturaProveedorPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new PagoFacturaProveedorAplicadoCommand(eventId, payload), cancellationToken);
                    break;
                }

                case PagoFacturaProveedorRevertidoHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<PagoFacturaProveedorRevertidoPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new PagoFacturaProveedorRevertidoCommand(eventId, payload), cancellationToken);
                    break;
                }

                case ReppProveedorRecibidoHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<ReppProveedorRecibidoPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new ReppProveedorRecibidoCommand(eventId, payload), cancellationToken);
                    break;
                }

                case CancelacionPasivoSolicitadaHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<CancelacionPasivoSolicitadaPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new CancelacionPasivoSolicitadaCommand(eventId, payload), cancellationToken);
                    break;
                }

                case PagoPrestamoViaticosAplicadoHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<PagoPrestamoViaticosAplicadoPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new PagoPrestamoViaticosAplicadoCommand(eventId, payload), cancellationToken);
                    break;
                }

                default:
                {
                    _logger.LogWarning(
                        "EventType no manejado por TesoreriaEventListener: {EventType}. Dead-letter.", eventType);
                    if (smbArgs is not null)
                    {
                        await smbArgs.DeadLetterMessageAsync(
                            smbArgs.Message, "UnknownEventType",
                            $"CxP no consume '{eventType}'.", cancellationToken);
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
            "TesoreriaEventListener processor error. Source={Source} Entity={EntityPath}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }
}
