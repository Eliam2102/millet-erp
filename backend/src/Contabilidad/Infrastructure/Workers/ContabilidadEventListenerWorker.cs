using System.Collections.Frozen;
using Azure.Messaging.ServiceBus;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Contabilidad.Infrastructure.Workers;

/// <summary>
/// C1.5 · base de los cuatro listeners de Contabilidad (un topic cada uno). Mismo patrón que
/// <c>CuentasPorPagar/Infrastructure/Workers/TesoreriaEventListenerWorker</c>: dedupe en <c>contabilidad.eventos_procesados</c>,
/// carta muerta para JSON inválido o tipo de otro módulo, abandono para reintento en errores técnicos y entrega local (C1.8).
/// <para>
/// Solo los tipos contables de <see cref="TiposContables"/> (la misma lista que el filtro de la suscripción en
/// <c>infra/modules/servicebus.bicep</c>) van al motor; los informativos del módulo se ignoran sin pasar por la bandeja, también en la
/// entrega local, que no aplica los filtros. Un tipo contable sin plantilla vigente queda con <c>SinPlantilla</c> (C1.5-i).
/// Un rechazo del motor (rol sin cuenta, periodo cerrado, dato faltante) también cuenta como procesado: el evento ya está en la
/// bandeja y se reprocesa desde ahí. La empresa la fija el propio evento (el motor filtra por su <c>EmpresaId</c>), no el usuario.
/// </para>
/// </summary>
public abstract class ContabilidadEventListenerWorker : BackgroundService, ILocalIntegrationEventConsumer
{
    private readonly ServiceBusClient? _serviceBusClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;
    private ServiceBusProcessor? _processor;

    protected ContabilidadEventListenerWorker(ServiceBusClient? serviceBusClient, IServiceScopeFactory scopeFactory, ILogger logger)
    {
        _serviceBusClient = serviceBusClient;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public abstract string Topic { get; }
    public abstract string Subscription { get; }

    /// <summary>Prefijo de los tipos del módulo dueño del topic (p. ej. <c>facturacion.</c>); otro prefijo es carta muerta.</summary>
    protected abstract string PrefijoModulo { get; }

    /// <summary>Tipos que generan póliza o anulación; igual que el <c>EventTypeFilter</c> de la suscripción.</summary>
    public abstract IReadOnlySet<string> TiposContables { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_serviceBusClient is null) return;
        var options = new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            // Uno a la vez: la liquidación de efectivo lee los cobros guardados y la reversa de consumo lee la salida.
            MaxConcurrentCalls = 1,
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5),
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
        };

        _processor = _serviceBusClient.CreateProcessor(Topic, Subscription, options);
        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += OnErrorAsync;

        try
        {
            await _processor.StartProcessingAsync(stoppingToken);
            _logger.LogInformation("{Worker} iniciado. Topic={Topic} Subscription={Sub}", GetType().Name, Topic, Subscription);
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
                catch (Exception ex) { _logger.LogWarning(ex, "Error parando {Worker}.", GetType().Name); }
            }
            if (_processor is not null)
            {
                await _processor.DisposeAsync();
                _processor = null;
            }
        }
    }

    private Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        var eventType = args.Message.Subject ?? string.Empty;
        var eventId = Guid.TryParse(args.Message.MessageId, out var parsed) ? parsed : Guid.NewGuid();
        return ProcessAsync(eventType, eventId, args.Message.Body.ToString(), args, args.CancellationToken);
    }

    internal async Task ProcessAsync(string eventType, Guid eventId, string body, ProcessMessageEventArgs? smbArgs, CancellationToken ct)
    {
        try
        {
            if (!eventType.StartsWith(PrefijoModulo, StringComparison.Ordinal))
            {
                _logger.Log(smbArgs is null ? LogLevel.Debug : LogLevel.Warning,
                    "{Worker}: EventType de otro módulo {EventType}. Dead-letter.", GetType().Name, eventType);
                if (smbArgs is not null)
                    await smbArgs.DeadLetterMessageAsync(smbArgs.Message, "UnknownEventType", $"Contabilidad no consume '{eventType}' en {Topic}.", ct);
                return;
            }

            if (!TiposContables.Contains(eventType))
            {
                _logger.LogDebug("{Worker}: evento informativo {EventType}, sin póliza. Se ignora.", GetType().Name, eventType);
                if (smbArgs is not null) await smbArgs.CompleteMessageAsync(smbArgs.Message, ct);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<ContabilidadDbContext>();
            using var origin = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(GetType().Name);
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();

            if (await db.EventosProcesados.AsNoTracking().AnyAsync(e => e.EventoId == eventId && e.EventoTipo == eventType, ct))
            {
                _logger.LogDebug("Evento ya procesado (dedupe). EventType={EventType} EventId={EventId}", eventType, eventId);
                if (smbArgs is not null) await smbArgs.CompleteMessageAsync(smbArgs.Message, ct);
                return;
            }

            var resultado = await sp.GetRequiredService<IMediator>().Send(new ProcesarEventoContableCommand(eventType, eventId, body), ct);
            if (resultado.Causa is { } causa)
                _logger.LogInformation("Evento a la bandeja contable. EventType={EventType} EventId={EventId} Causa={Causa} Detalle={Detalle}",
                    eventType, eventId, causa, resultado.Detalle);

            db.ChangeTracker.Clear();
            db.EventosProcesados.Add(new EventoContableProcesado(eventId, eventType, sp.GetRequiredService<IClock>().UtcNow));
            await db.SaveChangesAsync(ct);

            if (smbArgs is not null) await smbArgs.CompleteMessageAsync(smbArgs.Message, ct);
        }
        catch (BusinessRuleException ex) when (ex.Code == "CONTAB_EVENTO_ILEGIBLE")
        {
            _logger.LogError(ex, "Payload corrupto. EventType={EventType} EventId={EventId}. Dead-letter.", eventType, eventId);
            if (smbArgs is not null)
                await smbArgs.DeadLetterMessageAsync(smbArgs.Message, "InvalidPayload", ex.Message, ct);
        }
        catch (Exception ex)
        {
            // C1.8: en la entrega local no hay mensaje que abandonar; se relanza para que el outbox registre la falla y reintente.
            if (smbArgs is null) throw;
            _logger.LogError(ex, "Error procesando evento. EventType={EventType} EventId={EventId}. Abandono para retry.", eventType, eventId);
            await smbArgs.AbandonMessageAsync(smbArgs.Message, cancellationToken: ct);
        }
    }

    // C1.8 — entrega local sin Service Bus (solo Development/demo).
    string ILocalIntegrationEventConsumer.TopicName => Topic;

    Task ILocalIntegrationEventConsumer.ConsumirLocalAsync(string eventType, Guid eventId, string body, CancellationToken cancellationToken)
        => ProcessAsync(eventType, eventId, body, smbArgs: null, cancellationToken);

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "{Worker} processor error. Source={Source} Entity={EntityPath}",
            GetType().Name, args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }
}

/// <summary>C1.5 · <c>facturacion-events</c>: ventas, anticipos, NC, cancelaciones, cobros de mostrador y sesiones de caja (#7–#11).</summary>
public sealed class FacturacionContabilidadListenerWorker(ServiceBusClient? client, IServiceScopeFactory scopeFactory,
    ILogger<FacturacionContabilidadListenerWorker> logger) : ContabilidadEventListenerWorker(client, scopeFactory, logger)
{
    public const string TopicName = "facturacion-events";
    public const string SubscriptionName = "contabilidad-facturacion-sub";
    public override string Topic => TopicName;
    public override string Subscription => SubscriptionName;
    protected override string PrefijoModulo => "facturacion.";
    public override IReadOnlySet<string> TiposContables => Tipos;

    private static readonly FrozenSet<string> Tipos = FrozenSet.ToFrozenSet(
    [
        NormalizadorEventos.FacturaVentaTimbrada, NormalizadorEventos.FacturaAnticipoTimbrada, NormalizadorEventos.NotaCreditoTimbrada,
        NormalizadorEventos.ComprobanteCancelado, NormalizadorEventos.CobroMostradorRegistrado, NormalizadorEventos.CobroMostradorCancelado,
        NormalizadorEventos.CajaSesionCerrada,
    ], StringComparer.Ordinal);
}

/// <summary>C1.5 · <c>cuentas-por-pagar-events</c>: factura (#2, C1.4), cancelación, NC, nota de cargo y anticipo de proveedor (#3–#4).</summary>
public sealed class CuentasPorPagarContabilidadListenerWorker(ServiceBusClient? client, IServiceScopeFactory scopeFactory,
    ILogger<CuentasPorPagarContabilidadListenerWorker> logger) : ContabilidadEventListenerWorker(client, scopeFactory, logger)
{
    public const string TopicName = "cuentas-por-pagar-events";
    public const string SubscriptionName = "contabilidad-cuentas-por-pagar-sub";
    public override string Topic => TopicName;
    public override string Subscription => SubscriptionName;
    protected override string PrefijoModulo => "cuentas_por_pagar.";
    public override IReadOnlySet<string> TiposContables => Tipos;

    private static readonly FrozenSet<string> Tipos = FrozenSet.ToFrozenSet(
    [
        NormalizadorEventos.FacturaProveedorRegistrada, NormalizadorEventos.FacturaProveedorCancelada,
        NormalizadorEventos.NotaCreditoProveedorRegistrada, NormalizadorEventos.NotaCargoAutorizada, NormalizadorEventos.AnticipoProveedorCapturado,
    ], StringComparer.Ordinal);
}

/// <summary>C1.5 · <c>tesoreria-events</c>: pago a proveedor, su reversa y pago de cliente (#5, #6, #12).</summary>
public sealed class TesoreriaContabilidadListenerWorker(ServiceBusClient? client, IServiceScopeFactory scopeFactory,
    ILogger<TesoreriaContabilidadListenerWorker> logger) : ContabilidadEventListenerWorker(client, scopeFactory, logger)
{
    public const string TopicName = "tesoreria-events";
    public const string SubscriptionName = "contabilidad-tesoreria-sub";
    public override string Topic => TopicName;
    public override string Subscription => SubscriptionName;
    protected override string PrefijoModulo => "tesoreria.";
    public override IReadOnlySet<string> TiposContables => Tipos;

    private static readonly FrozenSet<string> Tipos = FrozenSet.ToFrozenSet(
    [
        NormalizadorEventos.PagoProveedorAplicado, NormalizadorEventos.PagoProveedorRevertido, NormalizadorEventos.PagoClienteConfirmado,
    ], StringComparer.Ordinal);
}

/// <summary>C1.5 · <c>almacen-events</c>: recepción y su cancelación, ajuste, salida, devoluciones interna y a proveedor (#1, #13–#15).</summary>
public sealed class AlmacenContabilidadListenerWorker(ServiceBusClient? client, IServiceScopeFactory scopeFactory,
    ILogger<AlmacenContabilidadListenerWorker> logger) : ContabilidadEventListenerWorker(client, scopeFactory, logger)
{
    public const string TopicName = "almacen-events";
    public const string SubscriptionName = "contabilidad-almacen-sub";
    public override string Topic => TopicName;
    public override string Subscription => SubscriptionName;
    protected override string PrefijoModulo => "almacen.";
    public override IReadOnlySet<string> TiposContables => Tipos;

    private static readonly FrozenSet<string> Tipos = FrozenSet.ToFrozenSet(
    [
        NormalizadorEventos.RecepcionOcRegistrada, NormalizadorEventos.RecepcionOcCancelada, NormalizadorEventos.AjusteInventarioAplicado,
        NormalizadorEventos.SalidaRequisicionRegistrada, NormalizadorEventos.DevolucionInternaAplicada, NormalizadorEventos.DevolucionProveedorRegistrada,
    ], StringComparer.Ordinal);
}
