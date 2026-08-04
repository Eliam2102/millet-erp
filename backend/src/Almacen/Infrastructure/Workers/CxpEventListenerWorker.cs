using System.Text.Json;
using Azure.Messaging.ServiceBus;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.Almacen.Application.EventListeners;
using Millet.Almacen.Domain.Idempotencia;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.Infrastructure.Workers;

/// <summary>
/// <c>BackgroundService</c> que se conecta al topic <c>cuentas-por-pagar-events</c>
/// (publicado por <c>OutboxPublisherWorker&lt;CuentasPorPagarDbContext&gt;</c>) y
/// procesa los eventos que Almacén suscribe (F3-PR1):
/// <list type="bullet">
///   <item><c>cuentas_por_pagar.factura.registrada.v1</c> — variante B
///   concilia recepción pendiente con factura.</item>
///   <item><c>cuentas_por_pagar.factura.diferencia-precio-detectada.v1</c> —
///   ajuste de costo del remanente A11.</item>
/// </list>
///
/// <para>
/// <b>Idempotencia (A12)</b>: el dedup vive en
/// <c>almacen.eventos_procesados</c> con PK <c>(evento_id, evento_tipo)</c>.
/// El worker verifica antes de despachar al handler; si el evento ya
/// fue procesado, completa el mensaje en Service Bus y descarta.
/// </para>
///
/// <para>
/// <b>Dead-letter</b>: payloads corruptos o handlers con excepción
/// permanente van a dead-letter queue. Errores transitorios (DbUpdate,
/// network) abandonan el mensaje para retry.
/// </para>
///
/// <para>
/// <b>Subscription</b>: el worker arranca con
/// <c>almacen-subscription</c>. La sub se crea manualmente en Service
/// Bus (Bicep / portal) con SQL filter
/// <c>EventType IN ('cuentas_por_pagar.factura.registrada.v1',
/// 'cuentas_por_pagar.factura.diferencia-precio-detectada.v1')</c> para
/// reducir el volumen entrante; sin filter el worker filtra en código
/// (defense-in-depth).
/// </para>
/// </summary>
public sealed class CxpEventListenerWorker : BackgroundService
{
    public const string TopicName = "cuentas-por-pagar-events";
    public const string SubscriptionName = "almacen-subscription";

    // PropertyNameCaseInsensitive = true: el OutboxSaveChangesInterceptor
    // serializa con PascalCase (no setea PropertyNamingPolicy en sus
    // JsonOptions). Sin case-insensitive, los Guid `FacturaProveedorId` /
    // `OrdenCompraId` del wire llegan al record como Guid.Empty y los
    // handlers explotan al buscar agregados inexistentes. Mismo bug que
    // ya rompió Compras→Almacén (#293+#299); este worker tenía el defecto
    // latente porque la subscription aún no había visto eventos productivos.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ServiceBusClient _serviceBusClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CxpEventListenerWorker> _logger;

    private ServiceBusProcessor? _processor;

    public CxpEventListenerWorker(
        ServiceBusClient serviceBusClient,
        IServiceScopeFactory scopeFactory,
        ILogger<CxpEventListenerWorker> logger)
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
                "CxpEventListener iniciado. Topic={Topic} Subscription={Sub}",
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
                    _logger.LogWarning(ex, "Error parando CxpEventListener processor.");
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
            var db = sp.GetRequiredService<AlmacenDbContext>();
            var mediator = sp.GetRequiredService<IMediator>();
            var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();
            using var bypass = empresaContext.Bypass();

            // Dedupe (A12).
            var yaProcesado = await db.Set<EventoProcesado>()
                .AsNoTracking()
                .AnyAsync(e => e.EventoId == eventId && e.EventoTipo == eventType,
                    cancellationToken);
            if (yaProcesado)
            {
                _logger.LogDebug(
                    "Evento ya procesado (dedupe). EventType={EventType} EventId={EventId}",
                    eventType, eventId);
                if (smbArgs is not null) await smbArgs.CompleteMessageAsync(smbArgs.Message, cancellationToken);
                return;
            }

            // Despacha por tipo.
            switch (eventType)
            {
                case FacturaProveedorRegistradaHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<FacturaProveedorRegistradaPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(
                        new FacturaProveedorRegistradaCommand(eventId, payload), cancellationToken);
                    break;
                }

                case DiferenciaPrecioFacturaDetectadaHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<DiferenciaPrecioFacturaDetectadaPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(
                        new DiferenciaPrecioFacturaDetectadaCommand(eventId, payload), cancellationToken);
                    break;
                }

                case NotaCreditoProveedorRegistradaHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<NotaCreditoProveedorRegistradaPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(
                        new NotaCreditoProveedorRegistradaCommand(eventId, payload), cancellationToken);
                    break;
                }

                case CfdiRecibidoIngresadoHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<CfdiRecibidoIngresadoPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(
                        new CfdiRecibidoIngresadoCommand(eventId, payload), cancellationToken);
                    break;
                }

                default:
                {
                    // EventType desconocido: dead-letter para investigación.
                    _logger.LogWarning(
                        "EventType no manejado por CxpEventListener: {EventType}. Dead-letter.", eventType);
                    if (smbArgs is not null)
                    {
                        await smbArgs.DeadLetterMessageAsync(
                            smbArgs.Message, "UnknownEventType",
                            $"Almacén no consume '{eventType}'.", cancellationToken);
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
            // No re-lanzamos — el SB processor maneja la lifecycle.
        }
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "CxpEventListener processor error. Source={Source} Entity={EntityPath}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }
}
