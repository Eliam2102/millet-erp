using System.Text.Json;
using Azure.Messaging.ServiceBus;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.Compras.Application.Almacen;
using Millet.Compras.Domain.Idempotencia;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Infrastructure.Workers;

/// <summary>
/// <c>BackgroundService</c> que conecta al topic <c>almacen-events</c>
/// (publicado por <c>OutboxPublisherWorker&lt;AlmacenDbContext&gt;</c>) y
/// procesa los eventos de Almacén que Compras suscribe:
///
/// <list type="bullet">
///   <item><c>almacen.oc_recepcion.registrada.v1</c> — incrementa
///   <c>CantidadRecibida</c> por línea de OC y recalcula sub-estado
///   Recepción (cierra OC si las 3 dimensiones llegan a Completa).</item>
///   <item><c>almacen.salida_requisicion.registrada.v1</c> (ADR-0043) —
///   acumula <c>CantidadEntregada</c> por línea de RQ (canal de entrega).</item>
/// </list>
///
/// <para>
/// Mismo patrón que <c>CxpEventListenerWorker</c> en Compras y
/// <c>AlmacenEventListenerWorker</c> en CxP: dedupe en
/// <see cref="EventoProcesado"/> antes de despachar; payloads corruptos
/// van a dead-letter; errores transitorios se abandonan para retry.
/// </para>
///
/// <para>
/// <b>Por qué este worker existe (bug histórico cerrado por PR &lt;CompasAlmacenListener&gt;)</b>:
/// hasta este punto Almacén publicaba el evento al outbox y CxP lo
/// consumía, pero Compras no — resultado: <c>LineaOrdenCompra.CantidadRecibida</c>
/// nunca se incrementaba y las recepciones parciales seguían mostrando
/// el pendiente original. Con este worker, Compras también consume el
/// evento y mantiene su agregado sincronizado.
/// </para>
///
/// <para>
/// <b>Subscription</b>: <c>compras-subscription-almacen</c> en topic
/// <c>almacen-events</c>. Creación vía Bicep / portal con SQL filter por
/// <c>EventType IN ('almacen.oc_recepcion.registrada.v1')</c>
/// recomendado (defense-in-depth: el worker filtra el `default` a
/// dead-letter).
/// </para>
///
/// <para>
/// <b>Empresa bypass</b>: corre fuera de request HTTP — usa
/// <see cref="ICurrentEmpresaContext.Bypass"/> para que el query filter
/// multi-tenant no excluya updates a la OC.
/// </para>
/// </summary>
public sealed class AlmacenEventListenerWorker : BackgroundService
{
    public const string TopicName = "almacen-events";
    public const string SubscriptionName = "compras-subscription-almacen";

    // PropertyNameCaseInsensitive = true: el OutboxSaveChangesInterceptor
    // serializa con PascalCase (no setea PropertyNamingPolicy en sus
    // JsonOptions). Sin case-insensitive, los `OrdenCompraId` / `RecepcionId`
    // del wire llegan al record como `Guid.Empty` y el handler explota con
    // EntityNotFoundException al buscar OC '00000000-...'. Case-insensitive
    // acepta camelCase Y PascalCase, blindando contra cualquier futuro cambio
    // de convención en el sender sin tocar consumidores.
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
                "AlmacenEventListener (Compras) iniciado. Topic={Topic} Subscription={Sub}",
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
                    _logger.LogWarning(ex, "Error parando AlmacenEventListener (Compras) processor.");
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
            var db = sp.GetRequiredService<ComprasDbContext>();
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
                case OcRecepcionEnAlmacenHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<OcRecepcionRegistradaAlmacenPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new OcRecepcionEnAlmacenCommand(eventId, payload), cancellationToken);
                    break;
                }

                // ADR-0043: canal de entrega. Acumula CantidadEntregada por
                // línea de RQ. El handler deduplica + muta la RQ en la misma TX.
                case SalidaRequisicionEnAlmacenHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<SalidaRequisicionRegistradaAlmacenPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new SalidaRequisicionEnAlmacenCommand(eventId, payload), cancellationToken);
                    break;
                }

                // GAP-5 (verificación e2e 2026-07-15): la devolución 8.B
                // decrementa CantidadRecibida por línea de OC (tabla
                // canónica de la triada). El handler calcula el acumulado
                // ajustado y publica el evento in-proc que consume
                // OcDevolucionRegistradaListener.
                case OcDevolucionEnAlmacenHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<OcDevolucionRegistradaAlmacenPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new OcDevolucionEnAlmacenCommand(eventId, payload), cancellationToken);
                    break;
                }

                // Eventos informativos que Almacén publica al mismo topic
                // pero Compras no consume todavía (saldos / cierres).
                // Loggeamos + marca de idempotencia para mantener el
                // outbox limpio.
                case "almacen.saldo.proyectado.v1":
                case "almacen.entrada-inventario.valorada.v1":
                case "almacen.salida-inventario.aplicada.v1":
                {
                    _logger.LogInformation(
                        "Evento Almacén informativo recibido por Compras (sin efecto). EventType={EventType} EventId={EventId}",
                        eventType, eventId);
                    db.Set<EventoProcesado>().Add(new EventoProcesado(
                        eventId, eventType, "Informativo — sin dispatch en Compras."));
                    await db.SaveChangesAsync(cancellationToken);
                    break;
                }

                default:
                {
                    _logger.LogWarning(
                        "EventType no manejado por AlmacenEventListener (Compras): {EventType}. Dead-letter.", eventType);
                    if (smbArgs is not null)
                    {
                        await smbArgs.DeadLetterMessageAsync(
                            smbArgs.Message, "UnknownEventType",
                            $"Compras no consume '{eventType}' del topic almacen-events.", cancellationToken);
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
            "AlmacenEventListener (Compras) processor error. Source={Source} Entity={EntityPath}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }
}
