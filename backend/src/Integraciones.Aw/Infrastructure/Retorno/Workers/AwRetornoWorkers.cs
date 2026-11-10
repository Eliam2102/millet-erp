using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.Integraciones.Aw.Application.Retorno;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Integraciones.Aw.Infrastructure.Retorno;

/// <summary>
/// TR-01: escucha <c>facturacion.factura-venta.timbrada.v1</c>, <c>facturacion.comprobante.cancelado.v1</c> y
/// <c>facturacion.nota-credito.timbrada.v1</c> (y, 86e3jvv6z, cobros de mostrador y REPP timbrados para las filas Pago) (topic facturacion-events) y deja la fila Documento
/// (FacturaFinal / Cancelacion / NotaCredito) en <c>aw_retorno</c>. La idempotencia vive en la tabla (UUID + tipo, candado por
/// pedido), no en una marca de evento: una reentrega no duplica. La nota de crédito solo genera fila si llegó de A+W
/// (86e3wjt5r: el evento trae pedido, versión y folio). Mismo patrón que <c>FacturacionEventListenerWorker</c> de CxC.
/// </summary>
public sealed class FacturacionRetornoListenerWorker : BackgroundService, ILocalIntegrationEventConsumer
{
    public const string TopicName = "facturacion-events";
    public const string SubscriptionName = "integraciones-aw-subscription";
    public const string EventTypeFacturaTimbrada = "facturacion.factura-venta.timbrada.v1";
    public const string EventTypeComprobanteCancelado = "facturacion.comprobante.cancelado.v1";
    public const string EventTypeNotaCreditoTimbrada = "facturacion.nota-credito.timbrada.v1";
    // 86e3jvv6z: pagos (tipo 4). El importe se recalcula de Facturación; el evento solo dice qué comprobante cambió.
    public const string EventTypeCobroRegistrado = "facturacion.cobro-mostrador.registrado.v1";
    public const string EventTypeCobroCancelado = "facturacion.cobro-mostrador.cancelado.v1";
    public const string EventTypeReciboPagoTimbrado = "facturacion.recibo-pago.timbrado.v1";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly ServiceBusClient _serviceBusClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FacturacionRetornoListenerWorker> _logger;
    private ServiceBusProcessor? _processor;

    public FacturacionRetornoListenerWorker(
        ServiceBusClient serviceBusClient, IServiceScopeFactory scopeFactory, ILogger<FacturacionRetornoListenerWorker> logger)
    {
        _serviceBusClient = serviceBusClient;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = _serviceBusClient.CreateProcessor(TopicName, SubscriptionName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 1, // el candado por pedido igual serializa; así no se pelean dos conexiones
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5),
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
        });
        _processor.ProcessMessageAsync += args => ProcessAsync(
            args.Message.Subject ?? string.Empty, args.Message.Body.ToString(), args, args.CancellationToken);
        _processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "FacturacionRetornoListener (Aw) error. Source={Source} Entity={Entity}", args.ErrorSource, args.EntityPath);
            return Task.CompletedTask;
        };

        try
        {
            await _processor.StartProcessingAsync(stoppingToken);
            _logger.LogInformation("FacturacionRetornoListener (Aw) iniciado. Topic={Topic} Subscription={Sub}", TopicName, SubscriptionName);
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            if (_processor is { IsProcessing: true }) await _processor.StopProcessingAsync(CancellationToken.None);
            if (_processor is not null) await _processor.DisposeAsync();
            _processor = null;
        }
    }

    string ILocalIntegrationEventConsumer.TopicName => TopicName;

    Task ILocalIntegrationEventConsumer.ConsumirLocalAsync(string eventType, Guid eventId, string body, CancellationToken cancellationToken)
        => ProcessAsync(eventType, body, null, cancellationToken);

    private static async Task RegistrarPagosAsync(AwRetornoDocumentoService servicio, string body, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<PagoRetornoPayload>(body, JsonOpts) ?? throw new JsonException("Payload null.");
        if ((p.ComprobanteId ?? p.ReciboPagoId) is { } id) await servicio.RegistrarPagosAsync(id, ct);
    }

    public static bool EsAviso(string eventType) =>
        eventType is EventTypeFacturaTimbrada or EventTypeComprobanteCancelado or EventTypeNotaCreditoTimbrada
            or EventTypeCobroRegistrado or EventTypeCobroCancelado or EventTypeReciboPagoTimbrado;

    /// <summary>Escribe en aw_retorno lo que pide el evento. Lo comparten el listener, el reintento de pendientes y el reproceso de dead-letter.</summary>
    internal static async Task AplicarAsync(AwRetornoDocumentoService servicio, string eventType, string body, CancellationToken ct)
    {
        if (eventType == EventTypeFacturaTimbrada)
        {
            var factura = JsonSerializer.Deserialize<FacturaTimbradaRetornoPayload>(body, JsonOpts)
                ?? throw new JsonException("Payload null.");
            await servicio.RegistrarFacturaAsync(factura, ct);
            // Un cobro que llegó antes que la factura no escribió nada: aquí se recogen sus pagos.
            if (factura.FacturaVentaId is { } id) await servicio.RegistrarPagosAsync(id, ct);
        }
        else if (eventType == EventTypeNotaCreditoTimbrada)
            await servicio.RegistrarNotaCreditoAsync(JsonSerializer.Deserialize<FacturaTimbradaRetornoPayload>(body, JsonOpts)
                ?? throw new JsonException("Payload null."), ct);
        else if (eventType == EventTypeComprobanteCancelado)
        {
            await servicio.RegistrarCancelacionAsync(JsonSerializer.Deserialize<CancelacionRetornoPayload>(body, JsonOpts)
                ?? throw new JsonException("Payload null."), ct);
            // Un REPP cancelado baja los pagos de sus facturas; una factura cancelada no deja pagos (se omite).
            await RegistrarPagosAsync(servicio, body, ct);
        }
        else
            await RegistrarPagosAsync(servicio, body, ct);
    }

    /// <summary>
    /// Internal para tests. M-160: si A+W no responde por conexión, el aviso queda pendiente (se reintenta solo) y el
    /// mensaje se completa; payload corrupto va a dead-letter; otra falla: sin Service Bus se relanza (el outbox reintenta),
    /// con él se abandona para retry.
    /// </summary>
    internal async Task ProcessAsync(string eventType, string body, ProcessMessageEventArgs? sb, CancellationToken ct)
    {
        try
        {
            if (EsAviso(eventType)
                && !await AwRetornoAplicador.AplicarOPendienteAsync(_scopeFactory, nameof(FacturacionRetornoListenerWorker), eventType, body, ct))
                _logger.LogWarning("A+W no responde; el aviso {EventType} queda pendiente de reintento.", eventType);
            if (sb is not null) await sb.CompleteMessageAsync(sb.Message, ct);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Payload corrupto. EventType={EventType}. Dead-letter.", eventType);
            if (sb is not null) await sb.DeadLetterMessageAsync(sb.Message, "InvalidPayload", ex.Message, ct);
        }
        catch (Exception ex)
        {
            if (sb is null) throw;
            _logger.LogError(ex, "Error procesando evento. EventType={EventType}. Abandono para retry.", eventType);
            await sb.AbandonMessageAsync(sb.Message, cancellationToken: ct);
        }
    }
}

/// <summary>
/// TR-01: cada 5 minutos lee el acuse de <c>aw_retorno</c>; reemplaza las desfasadas y avisa de las detenidas.
/// M-160: cada minuto reintenta los avisos pendientes cuyo siguiente intento ya venció (1, 5, 15, 60 min y luego cada hora).
/// </summary>
public sealed class AwRetornoAcuseWorker(IServiceScopeFactory scopeFactory, ILogger<AwRetornoAcuseWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(1);
    private const int PasadasPorConciliacion = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var pasada = 0; !stoppingToken.IsCancellationRequested; pasada++)
        {
            try { await ReintentarPendientesOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "AwRetornoAcuseWorker: reintento de pendientes falló; se reintenta en el siguiente intervalo."); }

            if (pasada % PasadasPorConciliacion == 0)
            {
                try { await ConciliarOnceAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "AwRetornoAcuseWorker: ciclo falló; se reintenta en el siguiente intervalo."); }
            }

            try { await Task.Delay(Intervalo, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task<AwRetornoConciliacion> ConciliarOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        using var origin = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(AwRetornoAcuseWorker));
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await sp.GetRequiredService<AwRetornoDocumentoService>().ConciliarAcusesAsync(ct);
    }

    /// <summary>
    /// Reintenta los pendientes vencidos en orden de llegada. Si A+W sigue sin responder, el resto de la pasada se cuenta
    /// como intento fallido con el mismo error sin volver a esperar el timeout de cada uno. Devuelve cuántos se aplicaron.
    /// </summary>
    internal async Task<int> ReintentarPendientesOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var pendientes = scope.ServiceProvider.GetRequiredService<AwRetornoPendientes>();
        string? caido = null;
        var aplicados = 0;
        foreach (var p in await pendientes.VencidosAsync(ct))
        {
            if (caido is not null) p.RegistrarFallo(caido, pendientes.Ahora);
            else
            {
                try
                {
                    await AwRetornoAplicador.AplicarAsync(scopeFactory, nameof(AwRetornoAcuseWorker), p.TipoEvento, p.Payload, ct);
                    p.MarcarAplicado(pendientes.Ahora);
                    aplicados++;
                }
                catch (Exception ex) when (AwRetornoAplicador.EsDeConexion(ex) && !ct.IsCancellationRequested)
                {
                    caido = AwRetornoAplicador.Mensaje(ex);
                    p.RegistrarFallo(caido, pendientes.Ahora);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "aw_retorno pendiente {Id} ({EventType}) detenido: la falla no es de conexión. Requiere revisión.", p.Id, p.TipoEvento);
                    p.Detener(AwRetornoAplicador.Mensaje(ex));
                }
            }
            await pendientes.GuardarCambiosAsync(ct); // por fila: lo aplicado no se repite si la pasada se corta
        }

        var resumen = await pendientes.ResumenAsync(ct);
        if (resumen.Alerta)
            logger.LogWarning("aw_retorno: {Pendientes} avisos esperan a A+W; el más antiguo ({EventType}) es de {Desde:u}. Último error: {Error}",
                resumen.Pendientes, resumen.MasAntiguoTipoEvento, resumen.MasAntiguoEnUtc, resumen.MasAntiguoUltimoError);
        return aplicados;
    }
}
