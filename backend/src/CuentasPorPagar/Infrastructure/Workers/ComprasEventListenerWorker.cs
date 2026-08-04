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
/// <c>BackgroundService</c> que se conecta al topic <c>compras-events</c>
/// (publicado por <c>OutboxPublisherWorker&lt;ComprasDbContext&gt;</c>) y
/// procesa los eventos OC que CxP suscribe:
/// <list type="bullet">
///   <item><c>compras.orden-compra.autorizada.v1</c> — registra que la
///   OC ya es facturable; usa <see cref="OcAutorizadaCommand"/>.</item>
///   <item><c>compras.orden-compra.cancelada.v1</c> — busca facturas
///   afectadas en estado Capturada/EnRevision y loggea alerta para
///   el Auxiliar; usa <see cref="OcCanceladaCommand"/>.</item>
/// </list>
///
/// <para>
/// Mismo patrón que <see cref="AlmacenEventListenerWorker"/> y
/// <see cref="TesoreriaEventListenerWorker"/>: dedupe en
/// <see cref="EventoProcesado"/> antes de despachar; payloads corruptos
/// van a dead-letter; errores transitorios se abandonan para retry. La
/// subscription <c>cuentas-por-pagar-subscription</c> en el topic
/// <c>compras-events</c> se crea vía Bicep/portal con filtro SQL por
/// <c>EventType IN ('compras.orden-compra.autorizada.v1',
/// 'compras.orden-compra.cancelada.v1')</c>.
/// </para>
///
/// <para>
/// <b>Empresa bypass</b>: el worker corre fuera de cualquier request
/// HTTP — el handler levanta <see cref="ICurrentEmpresaContext.Bypass"/>
/// para que el query filter multi-tenant no excluya las inserciones.
/// </para>
///
/// <para>
/// <b>OcCerradaEvent NO se consume aquí</b>: CxP no reacciona al cierre
/// final de OC porque ya no afecta su modelo. El cierre lo evalúa
/// Compras combinando los 3 sub-estados (Recepción + Facturación + Pago).
/// </para>
/// </summary>
public sealed class ComprasEventListenerWorker : BackgroundService
{
    public const string TopicName = "compras-events";
    public const string SubscriptionName = "cuentas-por-pagar-subscription";

    // PropertyNameCaseInsensitive = true: el OutboxSaveChangesInterceptor
    // serializa con PascalCase (no setea PropertyNamingPolicy en sus
    // JsonOptions). Sin case-insensitive, los Guid `OrdenCompraId` /
    // `CompradorTitularId` del wire llegan al record como Guid.Empty y los
    // handlers OcAutorizada/OcCancelada loggean OC '00000000-...'. Mismo
    // bug latente que #299 cerró en los workers de Almacén — este worker
    // entró con PR #285 antes del fix y mantenía el defecto.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ServiceBusClient _serviceBusClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ComprasEventListenerWorker> _logger;

    private ServiceBusProcessor? _processor;

    public ComprasEventListenerWorker(
        ServiceBusClient serviceBusClient,
        IServiceScopeFactory scopeFactory,
        ILogger<ComprasEventListenerWorker> logger)
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
                "ComprasEventListener iniciado. Topic={Topic} Subscription={Sub}",
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
                    _logger.LogWarning(ex, "Error parando ComprasEventListener processor.");
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
                case OcAutorizadaHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<OcAutorizadaPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new OcAutorizadaCommand(eventId, payload), cancellationToken);
                    break;
                }

                case OcCanceladaHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<OcCanceladaPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new OcCanceladaCommand(eventId, payload), cancellationToken);
                    break;
                }

                default:
                {
                    _logger.LogWarning(
                        "EventType no manejado por ComprasEventListener: {EventType}. Dead-letter.", eventType);
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
            "ComprasEventListener processor error. Source={Source} Entity={EntityPath}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }
}
