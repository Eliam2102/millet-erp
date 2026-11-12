using System.Text.Json;
using Azure.Messaging.ServiceBus;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.ComercioExterior.Application;
using Millet.Almacen.Application.Integration;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.ComercioExterior.Infrastructure.Workers;

/// <summary>Concilia recepciones de Almacén en CE; deduplica por evento.</summary>
public sealed class AlmacenEventListenerWorker : BackgroundService, ILocalIntegrationEventConsumer
{
    public const string TopicName = "almacen-events";
    public const string SubscriptionName = "comercio-exterior-almacen-sub";

    // Acepta PascalCase del Outbox y camelCase de productores del mismo contrato.
    // El handler valida las referencias de empresa, expediente y recepción.
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
        var eventId = Guid.TryParse(args.Message.MessageId, out var parsed) ? parsed : Guid.Empty;
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
            using var origin = originContext.SetOrigin(nameof(AlmacenEventListenerWorker));
            var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();
            using var bypass = empresaContext.Bypass();

            RecepcionCePayload? payload = null;
            switch (eventType)
            {
                case "almacen.oc_recepcion.registrada.v1":
                    payload = JsonSerializer.Deserialize<RecepcionCePayload>(body, JsonOpts) ?? throw new JsonException("Payload null.");
                    break;
                case "almacen.recepcion_oc.cancelada.v1":
                    var cancelada = JsonSerializer.Deserialize<OcRecepcionCanceladaIntegrationEvent>(body, JsonOpts) ?? throw new JsonException("Payload null.");
                    payload = new(cancelada.EmpresaId, cancelada.OrdenCompraId, cancelada.FechaMovimiento, cancelada.OcurridoEn,
                        cancelada.Lineas?.Select(l => new LineaRecepcionCe(l.LineaOcId, l.RecibidoAcumuladoDocumento)).ToArray()
                            ?? throw new JsonException("Faltan las líneas de la cancelación."), cancelada.CanceladaPor);
                    break;
                case "almacen.recepcion_activo.anulada.v1":
                    var anulada = JsonSerializer.Deserialize<RecepcionActivoAnuladaIntegrationEvent>(body, JsonOpts) ?? throw new JsonException("Payload null.");
                    payload = new(anulada.EmpresaId, anulada.OrdenCompraId, FechaContable.HoyLocal(anulada.OcurridoEn), anulada.OcurridoEn,
                        [new(anulada.LineaOcId, anulada.CantidadRecibidaAcumulada)]);
                    break;
                case "almacen.oc_devolucion.registrada.v1":
                    var devuelta = JsonSerializer.Deserialize<OcDevolucionRegistradaIntegrationEvent>(body, JsonOpts) ?? throw new JsonException("Payload null.");
                    if (devuelta.OrdenCompraOrigenId is Guid oc)
                        payload = new(devuelta.EmpresaId, oc, FechaContable.HoyLocal(devuelta.OcurridoEn), devuelta.OcurridoEn,
                            devuelta.Lineas?.Select(l => new LineaRecepcionCe(l.LineaOcId, null)).ToArray()
                                ?? throw new JsonException("Faltan las líneas de la devolución."));
                    break;
            }
            if (payload is not null)
                await mediator.Send(new RecibirRecepcionCeCommand(eventId, payload), cancellationToken);

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
            if (smbArgs is null) throw;
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
            "AlmacenEventListener processor error. Source={Source} Entity={EntityPath}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }
}
