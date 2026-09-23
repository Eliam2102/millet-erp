using System.Text.Json;
using Azure.Messaging.ServiceBus;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Application.EventListeners;
using Millet.CuentasPorPagar.Domain.Eventos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Infrastructure.Workers;

/// <summary>
/// <c>BackgroundService</c> que se conecta al topic <c>almacen-events</c>
/// (publicado por <c>OutboxPublisherWorker&lt;AlmacenDbContext&gt;</c>) y
/// procesa los eventos que CxP suscribe:
/// <list type="bullet">
///   <item><c>almacen.oc_recepcion.registrada.v1</c> — proyecta a
///   <c>recepciones_oc_local</c> (handler F5-PR1).</item>
///   <item><c>almacen.oc_devolucion.registrada.v1</c> — crea
///   <c>NotaCargo</c> Borrador (handler F6-PR3).</item>
/// </list>
///
/// <para>
/// Mismo patrón que <c>Almacen.Infrastructure.Workers.CxpEventListenerWorker</c>:
/// dedupe en <see cref="EventoProcesado"/> antes de despachar; payloads
/// corruptos van a dead-letter; errores transitorios se abandonan para
/// retry. La subscription <c>cuentas-por-pagar-subscription</c> se crea
/// vía Bicep/portal con filtro SQL por <c>EventType IN (...)</c>.
/// </para>
///
/// <para>
/// <b>Empresa bypass</b>: el worker corre fuera de cualquier request
/// HTTP — el handler levanta <see cref="ICurrentEmpresaContext.Bypass"/>
/// para que el query filter multi-tenant no excluya las inserciones.
/// </para>
/// </summary>
public sealed class AlmacenEventListenerWorker : BackgroundService
{
    public const string TopicName = "almacen-events";
    public const string SubscriptionName = "cuentas-por-pagar-subscription";

    // PropertyNameCaseInsensitive = true: el OutboxSaveChangesInterceptor
    // serializa con PascalCase (no setea PropertyNamingPolicy en sus
    // JsonOptions). Sin case-insensitive, los Guid de OrdenCompraId /
    // RecepcionId llegaban como Guid.Empty (bug detectado al validar
    // PR #293 en dev — el worker de Compras tenía el mismo defecto y se
    // arregló junto con éste).
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ServiceBusClient _serviceBusClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AlmacenEventListenerWorker> _logger;

    private ServiceBusProcessor? _processor;

    public AlmacenEventListenerWorker(
        ServiceBusClient serviceBusClient,
        IServiceScopeFactory scopeFactory,
        ILogger<AlmacenEventListenerWorker> logger)
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
                "AlmacenEventListener iniciado. Topic={Topic} Subscription={Sub}",
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
                    _logger.LogWarning(ex, "Error parando AlmacenEventListener processor.");
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
            var db = sp.GetRequiredService<CuentasPorPagarDbContext>();
            var mediator = sp.GetRequiredService<IMediator>();
            var originContext = sp.GetRequiredService<IAuditOriginContext>();
            using var origin = originContext.SetOrigin(nameof(AlmacenEventListenerWorker));
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
                case OcRecepcionRegistradaHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<OcRecepcionRegistradaPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new OcRecepcionRegistradaCommand(eventId, payload), cancellationToken);
                    break;
                }

                case OcDevolucionRegistradaHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<OcDevolucionRegistradaPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new OcDevolucionRegistradaCommand(eventId, payload), cancellationToken);
                    break;
                }

                default:
                {
                    _logger.LogWarning(
                        "EventType no manejado por AlmacenEventListener: {EventType}. Dead-letter.", eventType);
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
            "AlmacenEventListener processor error. Source={Source} Entity={EntityPath}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }
}
