using System.Text.Json;
using Azure.Messaging.ServiceBus;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.Compras.Application.Oc.Eventos.Cxp;
using Millet.Compras.Domain.Idempotencia;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Infrastructure.Workers;

/// <summary>
/// <c>BackgroundService</c> que se conecta al topic
/// <c>cuentas-por-pagar-events</c> (publicado por
/// <c>OutboxPublisherWorker&lt;CuentasPorPagarDbContext&gt;</c>) y procesa
/// los eventos de CxP que Compras suscribe (PR D — cierre outbound
/// CxP → Compras). El listener orphan
/// <c>FacturaProveedorRegistradaListener</c> antes huérfano queda wireado
/// vía el command despachado por este worker.
///
/// <list type="bullet">
///   <item><c>cuentas_por_pagar.factura.registrada.v1</c> — actualiza
///   sub-estado Facturación de OC vía
///   <c>OrdenCompra.RegistrarFacturacionLinea</c> por cada línea de OC
///   incluida en el payload (acumulado calculado en CxP).</item>
///   <item>Eventos informativos (loggeados + idempotency mark, sin
///   dispatch a domain): cancelación / rechazo / autorización de factura,
///   nota de crédito, nota de cargo, anticipo, diferencia de precio,
///   pasivo autorizado, cierre de TC. Compras los acepta pero su
///   semántica no impacta sub-estados de OC en el MVP — quedan como
///   PLATFORM-TODO para futuras reglas.</item>
/// </list>
///
/// <para>
/// <b>Idempotencia</b>: dedupe contra <c>compras.eventos_procesados</c>
/// (PK <c>(evento_id, evento_tipo)</c>). El worker verifica antes de
/// despachar; los handlers también insertan la marca dentro de la misma
/// TX EF que aplica el efecto.
/// </para>
///
/// <para>
/// <b>NO consumido en este PR</b>:
/// <list type="bullet">
///   <item><c>cuentas_por_pagar.nota-credito.registrada.v1</c> →
///   loggeado como informativo. NC en CxP no tiene granularidad por
///   línea (solo Total); el listener orphan
///   <c>NotaCreditoProveedorRegistradaListener</c> espera
///   <c>CantidadFacturadaAcumuladaAjustada</c> por línea, lo cual
///   requiere rediseño de modelo CxP. PLATFORM-TODO
///   <c>&lt;NcGranularidadLineaOc&gt;</c>.</item>
///   <item><c>tesoreria.pago-factura-proveedor.aplicado.v1</c> — vive en
///   el topic <c>tesoreria-events</c>, no en este topic. El listener
///   orphan <c>PagoFacturaProveedorListener</c> requiere proyección
///   local de facturas en Compras (Tesorería es externo y no enriquece
///   el payload con OcId ni acumulado). PLATFORM-TODO
///   <c>&lt;TesoreriaEventListenerCompras&gt;</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Subscription</b>: <c>compras-subscription</c> en topic
/// <c>cuentas-por-pagar-events</c>. Creación vía Bicep / portal con
/// SQL filter por <c>EventType</c> recomendado (defense-in-depth: el
/// worker filtra el `default` a dead-letter).
/// </para>
///
/// <para>
/// <b>Empresa bypass</b>: corre fuera de request HTTP — usa
/// <see cref="ICurrentEmpresaContext.Bypass"/> para que el query filter
/// multi-tenant no excluya las inserciones / updates.
/// </para>
/// </summary>
public sealed class CxpEventListenerWorker : BackgroundService
{
    public const string TopicName = "cuentas-por-pagar-events";
    public const string SubscriptionName = "compras-subscription";

    // PropertyNameCaseInsensitive = true: el OutboxSaveChangesInterceptor
    // de CxP serializa con PascalCase (no setea PropertyNamingPolicy en sus
    // JsonOptions). Sin case-insensitive, ninguna propiedad matchea: los
    // Guid llegan como Guid.Empty y `LineasAcumuladasOc` como null — el
    // handler de `factura.registrada.v1` explota con NullReference y el
    // mensaje termina en dead-letter (el sub-estado Facturación de la OC
    // nunca avanza). Case-insensitive acepta camelCase Y PascalCase,
    // blindando contra cualquier futuro cambio de convención en el sender
    // sin tocar consumidores — mismo criterio que el resto de los event
    // listeners de la solución. Internal para poder testear la
    // deserialización con el payload real del wire.
    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
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
                "CxpEventListener (Compras) iniciado. Topic={Topic} Subscription={Sub}",
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
                    _logger.LogWarning(ex, "Error parando CxpEventListener (Compras) processor.");
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
                case FacturaProveedorRegistradaCommandHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<FacturaProveedorRegistradaPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new FacturaProveedorRegistradaCommand(eventId, payload), cancellationToken);
                    break;
                }

                case FacturaPagoAplicadoCommandHandler.EventType:
                {
                    var payload = JsonSerializer.Deserialize<FacturaPagoAplicadoPayload>(body, JsonOpts)
                        ?? throw new JsonException("Payload null.");
                    await mediator.Send(new FacturaPagoAplicadoCommand(eventId, payload), cancellationToken);
                    break;
                }

                // Informativos — Compras los acepta para mantener el outbox limpio
                // y dejar marca de idempotencia, pero no aplica reglas de dominio
                // sobre la OC. Si en el futuro alguno cambia sub-estado, mover
                // a un command dedicado.
                case "cuentas_por_pagar.factura.cancelada.v1":
                case "cuentas_por_pagar.factura.autorizada.v1":
                case "cuentas_por_pagar.factura.rechazada-por-tolerancia.v1":
                case "cuentas_por_pagar.factura.diferencia-precio-detectada.v1":
                case "cuentas_por_pagar.nota-credito.registrada.v1":
                case "cuentas_por_pagar.nota-cargo.autorizada.v1":
                case "cuentas_por_pagar.anticipo.capturado.v1":
                case "cuentas_por_pagar.pasivo.autorizado-para-pago.v1":
                case "cuentas_por_pagar.estado-cuenta-tc.cerrado.v1":
                {
                    _logger.LogInformation(
                        "Evento CxP informativo recibido (sin efecto en OC). EventType={EventType} EventId={EventId}",
                        eventType, eventId);
                    db.Set<EventoProcesado>().Add(new EventoProcesado(eventId, eventType, "Informativo — sin dispatch."));
                    await db.SaveChangesAsync(cancellationToken);
                    break;
                }

                default:
                {
                    _logger.LogWarning(
                        "EventType no manejado por CxpEventListener (Compras): {EventType}. Dead-letter.", eventType);
                    if (smbArgs is not null)
                    {
                        await smbArgs.DeadLetterMessageAsync(
                            smbArgs.Message, "UnknownEventType",
                            $"Compras no consume '{eventType}'.", cancellationToken);
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
            "CxpEventListener (Compras) processor error. Source={Source} Entity={EntityPath}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }
}
