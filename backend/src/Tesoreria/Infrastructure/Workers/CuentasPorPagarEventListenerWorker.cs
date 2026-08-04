using System.Text.Json;
using Azure.Messaging.ServiceBus;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Application.EventListeners;
using Millet.Tesoreria.Domain.Eventos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Infrastructure.Workers;

/// <summary>
/// <c>BackgroundService</c> que se conecta al topic
/// <c>cuentas-por-pagar-events</c> y alimenta la proyección
/// <c>pasivo_pendiente_pago</c> (TES-PR3, 01-diseño §8.2/§9): cada
/// <c>pasivo.autorizado-para-pago.v1</c> se upserta a la bandeja de
/// egresos. En PR-6 este mismo listener agrega la sugerencia de liga
/// tardía cuando el proveedor tiene un pago a cuenta abierto.
///
/// <para>
/// Mismo patrón que <c>FacturacionEventListenerWorker</c> de CxC: dedupe
/// en <see cref="EventoProcesado"/> antes de despachar; payloads corruptos
/// a dead-letter; errores transitorios se abandonan para retry
/// (<c>MaxDeliveryCount=5</c>). La subscription <c>tesoreria-subscription</c>
/// se crea vía Bicep con filtro SQL por <c>EventType</c>.
/// </para>
///
/// <para>
/// <b>Empresa bypass</b>: el worker corre fuera de cualquier request
/// HTTP — levanta <see cref="ICurrentEmpresaContext.Bypass"/> para que el
/// query filter multi-tenant no excluya las inserciones.
/// </para>
/// </summary>
public sealed class CuentasPorPagarEventListenerWorker : BackgroundService
{
    public const string TopicName = "cuentas-por-pagar-events";
    public const string SubscriptionName = "tesoreria-subscription";

    // PropertyNameCaseInsensitive: el OutboxSaveChangesInterceptor serializa
    // con PascalCase (mismo bug detectado en PR #293 — sin esto los Guid
    // llegan como Guid.Empty).
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ServiceBusClient _serviceBusClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CuentasPorPagarEventListenerWorker> _logger;

    private ServiceBusProcessor? _processor;

    public CuentasPorPagarEventListenerWorker(
        ServiceBusClient serviceBusClient,
        IServiceScopeFactory scopeFactory,
        ILogger<CuentasPorPagarEventListenerWorker> logger)
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
                "CuentasPorPagarEventListener (Tesorería) iniciado. Topic={Topic} Subscription={Sub}",
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
                    _logger.LogWarning(ex, "Error parando CuentasPorPagarEventListener processor.");
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
            var db = sp.GetRequiredService<TesoreriaDbContext>();
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
                case PasivoAutorizadoParaPagoPayload.EventType:
                {
                    var payload = JsonSerializer.Deserialize<PasivoAutorizadoParaPagoPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    // GI-PR2: los pasivos internos (EsPasivoInterno) también
                    // se proyectan — la rama vive en el handler (upsert por
                    // origen). Cerró PLATFORM-TODO(<PasivosInternosBandeja>).
                    await mediator.Send(new ProyectarPasivoAutorizadoCommand(eventId, payload), cancellationToken);
                    break;
                }

                case DepositoViaticosEsperadoPayload.EventType:
                {
                    var payload = JsonSerializer.Deserialize<DepositoViaticosEsperadoPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new ProyectarDepositoViaticosCommand(eventId, payload), cancellationToken);
                    break;
                }

                default:
                {
                    _logger.LogWarning(
                        "EventType no manejado por CuentasPorPagarEventListener (Tesorería): {EventType}. Dead-letter.",
                        eventType);
                    if (smbArgs is not null)
                    {
                        await smbArgs.DeadLetterMessageAsync(
                            smbArgs.Message, "UnknownEventType",
                            $"Tesorería no consume '{eventType}'.", cancellationToken);
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
            "CuentasPorPagarEventListener (Tesorería) processor error. Source={Source} Entity={EntityPath}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }
}
