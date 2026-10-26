using System.Security.Claims;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;
using Millet.ActivosFijos.Application;
namespace Millet.ActivosFijos.Infrastructure.Workers;
public sealed class VentaActivoWorker(ServiceBusClient client, IServiceScopeFactory scopes, ILogger<VentaActivoWorker> logger)
    : BackgroundService, ILocalIntegrationEventConsumer
{
    public const string TopicName = "facturacion-events";
    private const string Timbrada = "facturacion.factura-venta.timbrada.v1";
    private const string Cancelado = "facturacion.comprobante.cancelado.v1";

    string ILocalIntegrationEventConsumer.TopicName => TopicName;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var processor = client.CreateProcessor(TopicName, "activos-fijos-ventas", new ServiceBusProcessorOptions { AutoCompleteMessages = false, MaxConcurrentCalls = 1 });
        processor.ProcessMessageAsync += ProcesarAsync;
        processor.ProcessErrorAsync += e => { logger.LogError(e.Exception, "Error procesando venta de activo"); return Task.CompletedTask; };
        await processor.StartProcessingAsync(stoppingToken);
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { await processor.StopProcessingAsync(CancellationToken.None); }
    }
    private async Task ProcesarAsync(ProcessMessageEventArgs e)
    {
        if (e.Message.Subject is Timbrada or Cancelado)
        {
            var (empresa, factura) = Leer(e.Message.Subject, e.Message.Body.ToString());
            await ProcesarVentaAsync(empresa, factura, e.CancellationToken);
        }
        await e.CompleteMessageAsync(e.Message, e.CancellationToken);
    }

    /// <summary>
    /// C1.8 — entrega local sin Service Bus. Sin <c>ProcessMessageEventArgs</c> no hay
    /// abandono: la falla del consumidor se propaga para que el outbox reintente la fila;
    /// un payload inválido se descarta (reintentar no lo corrige).
    /// </summary>
    async Task ILocalIntegrationEventConsumer.ConsumirLocalAsync(string eventType, Guid eventId, string body, CancellationToken cancellationToken)
    {
        if (eventType is not (Timbrada or Cancelado)) return;
        (Guid Empresa, Guid Factura) datos;
        try { datos = Leer(eventType, body); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            logger.LogError(ex, "Payload inválido en entrega local. EventType={EventType} EventId={EventId}. Se descarta.", eventType, eventId);
            return;
        }
        await ProcesarVentaAsync(datos.Empresa, datos.Factura, cancellationToken);
    }

    private static (Guid Empresa, Guid Factura) Leer(string eventType, string body)
    {
        using var json = JsonDocument.Parse(body);
        Guid Campo(string nombre) => json.RootElement.EnumerateObject().First(x => x.Name.Equals(nombre, StringComparison.OrdinalIgnoreCase)).Value.GetGuid();
        return (Campo("EmpresaId"), Campo(eventType == Timbrada ? "FacturaVentaId" : "ComprobanteId"));
    }

    private async Task ProcesarVentaAsync(Guid empresa, Guid factura, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        using var origen = scope.ServiceProvider.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(VentaActivoWorker));
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var anterior = accessor.HttpContext;
        try
        {
            // Contexto de empresa explícito: el consumidor nunca desactiva el filtro multiempresa.
            accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(MilletClaimTypes.CurrentEmpresaId, empresa.ToString())], "ActivosFijosWorker")) };
            await scope.ServiceProvider.GetRequiredService<VentaActivoConsumer>().ProcesarAsync(factura, ct);
        }
        finally { accessor.HttpContext = anterior; }
    }
}
